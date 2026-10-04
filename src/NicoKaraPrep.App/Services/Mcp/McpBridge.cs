using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// MCP の橋渡し（<c>NicoKaraPrep.exe --mcp</c>）。MCP クライアント（Claude Code など）とは標準入出力（stdio）で話し、
/// 受け取った JSON-RPC のメッセージを、起動中の本体の名前付きパイプ（<see cref="McpHost"/>）へそのまま流す。
/// 本体が起動していないあいだも応答する: initialize と道具の一覧は自分で返し（一覧は本体と同じ定義から作る）、
/// 道具の呼び出しには「起動してください」と答える。本体があとから起動すれば、次の呼び出しからつながる。
/// 標準出力は JSON-RPC 専用（記録は標準エラーへ）。
/// </summary>
internal static class McpBridge
{
    private const string BridgeInitializeId = "nicokaraprep-bridge-initialize";
    private const string DefaultProtocolVersion = "2025-06-18";

    public static bool IsBridgeInvocation(string[] args) => args.Any(a => a.Equals("--mcp", StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        bool verbose = args.Any(a => a.Equals("--mcp-verbose", StringComparison.OrdinalIgnoreCase));
        try
        {
            return new Relay(verbose).RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NicoKaraPrep --mcp] {ex}");
            return 1;
        }
    }

    private sealed class Relay
    {
        private readonly bool _verbose;
        private readonly StreamWriter _stdout;
        private readonly SemaphoreSlim _stdoutGate = new(1, 1);
        private readonly SemaphoreSlim _pipeGate = new(1, 1);
        private readonly object _stateLock = new();

        private NamedPipeClientStream? _pipe;
        private StreamWriter? _pipeWriter;
        private TaskCompletionSource<bool>? _handshake;
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private JsonNode? _clientInfo;
        private JsonNode? _clientCapabilities;
        private string _protocolVersion = DefaultProtocolVersion;
        private string? _toolsJson;

        public Relay(bool verbose)
        {
            _verbose = verbose;
            _stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public async Task<int> RunAsync()
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            Log($"start pipe={McpInfo.PipeName}");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Trim().Length == 0) continue;
                try
                {
                    await HandleClientLineAsync(line);
                }
                catch (Exception ex)
                {
                    Log($"client line failed: {ex.Message}");
                }
            }
            Log("stdin closed");
            await DisconnectAsync("クライアントが終了しました");
            return 0;
        }

        // ------------------------------------------------------------ クライアント（stdio）からのメッセージ

        private async Task HandleClientLineAsync(string line)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                await WriteClientAsync(Error(null, -32700, "JSON を読めませんでした"));
                return;
            }
            if (node is not JsonObject message)
            {
                return; // 配列（バッチ）には対応しない
            }
            string? method = message["method"]?.GetValue<string>();
            JsonNode? id = message["id"];
            bool isRequest = id is not null && method is not null;
            bool isNotification = id is null && method is not null;

            if (method == "initialize" && isRequest)
            {
                var p = message["params"] as JsonObject;
                _clientInfo = p?["clientInfo"]?.DeepClone();
                _clientCapabilities = p?["capabilities"]?.DeepClone();
                if (p?["protocolVersion"]?.GetValue<string>() is { Length: > 0 } v) _protocolVersion = v;
                await WriteClientAsync(Result(id, new JsonObject
                {
                    ["protocolVersion"] = _protocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = McpInfo.ServerName, ["version"] = McpInfo.Version },
                    ["instructions"] = McpInfo.Instructions,
                }));
                return;
            }
            if (method == "notifications/initialized" && isNotification) return;
            if (method == "ping" && isRequest)
            {
                await WriteClientAsync(Result(id, new JsonObject()));
                return;
            }

            // 応答（クライアント → サーバーの要求への返事）は本体へ流す
            if (method is null)
            {
                if (await EnsureConnectedAsync()) await WritePipeAsync(line);
                return;
            }

            if (await EnsureConnectedAsync())
            {
                if (isRequest) Track(id!, add: true);
                await WritePipeAsync(line);
                return;
            }

            // 本体が無いとき
            if (isNotification) return;
            switch (method)
            {
                case "tools/list":
                    await WriteClientAsync(Result(id, new JsonObject { ["tools"] = JsonNode.Parse(ToolsJson()) }));
                    break;
                case "tools/call":
                    await WriteClientAsync(Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = McpInfo.NotRunningMessage }),
                        ["isError"] = true,
                    }));
                    break;
                default:
                    await WriteClientAsync(Error(id, -32601, $"{method} は本体が起動していないと使えません。{McpInfo.NotRunningMessage}"));
                    break;
            }
        }

        /// <summary>道具の一覧（本体と同じ定義。本体が無いときに返す）。</summary>
        private string ToolsJson()
        {
            if (_toolsJson is not null) return _toolsJson;
            var tools = McpToolCatalog.Create(null).Select(t => t.ProtocolTool).ToList();
            _toolsJson = JsonSerializer.Serialize(tools, McpJsonUtilities.DefaultOptions);
            return _toolsJson;
        }

        // ------------------------------------------------------------ 本体（パイプ）との接続

        private async Task<bool> EnsureConnectedAsync()
        {
            await _pipeGate.WaitAsync();
            try
            {
                if (_pipe is { IsConnected: true } && _handshake?.Task is { IsCompletedSuccessfully: true, Result: true }) return true;
                if (_pipe is not null) await DisconnectCoreAsync("本体との接続が切れました");

                var pipe = new NamedPipeClientStream(".", McpInfo.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.ConnectAsync(400);
                }
                catch (Exception ex)
                {
                    Log($"connect failed: {ex.GetType().Name}");
                    pipe.Dispose();
                    return false;
                }
                _pipe = pipe;
                _pipeWriter = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                _handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = ReadPipeLoopAsync(pipe);

                // 本体のセッションを始める（クライアントの名前と版をそのまま名乗る）
                var init = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = BridgeInitializeId,
                    ["method"] = "initialize",
                    ["params"] = new JsonObject
                    {
                        ["protocolVersion"] = _protocolVersion,
                        ["capabilities"] = _clientCapabilities?.DeepClone() ?? new JsonObject(),
                        ["clientInfo"] = _clientInfo?.DeepClone() ?? new JsonObject { ["name"] = "NicoKaraPrep MCP bridge", ["version"] = McpInfo.Version },
                    },
                };
                await _pipeWriter.WriteLineAsync(init.ToJsonString());
                var completed = await Task.WhenAny(_handshake.Task, Task.Delay(10000));
                if (completed != _handshake.Task || !_handshake.Task.Result)
                {
                    Log("handshake failed");
                    await DisconnectCoreAsync("本体がセッションを始められませんでした");
                    return false;
                }
                await _pipeWriter.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString());
                Log("connected");
                return true;
            }
            finally
            {
                _pipeGate.Release();
            }
        }

        private async Task ReadPipeLoopAsync(NamedPipeClientStream pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1 << 16, leaveOpen: true);
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.Trim().Length == 0) continue;
                    JsonObject? message = null;
                    try
                    {
                        message = JsonNode.Parse(line) as JsonObject;
                    }
                    catch (JsonException)
                    {
                        // 読めない行はそのまま流す
                    }
                    if (message?["id"] is { } id && message["method"] is null && IdString(id) == BridgeInitializeId)
                    {
                        _handshake?.TrySetResult(message["result"] is not null);
                        continue;
                    }
                    if (message?["id"] is { } responseId && message["method"] is null) Track(responseId, add: false);
                    await WriteClientAsync(line);
                }
            }
            catch (Exception ex)
            {
                Log($"pipe read ended: {ex.GetType().Name}");
            }
            if (ReferenceEquals(_pipe, pipe))
            {
                await _pipeGate.WaitAsync();
                try
                {
                    if (ReferenceEquals(_pipe, pipe)) await DisconnectCoreAsync("本体との接続が切れました（本体を閉じた、など）");
                }
                finally
                {
                    _pipeGate.Release();
                }
            }
        }

        private async Task DisconnectAsync(string reason)
        {
            await _pipeGate.WaitAsync();
            try
            {
                await DisconnectCoreAsync(reason);
            }
            finally
            {
                _pipeGate.Release();
            }
        }

        /// <summary>本体との接続を閉じ、返事を待っていたクライアントの要求にはエラーを返す（_pipeGate の中で呼ぶ）。</summary>
        private async Task DisconnectCoreAsync(string reason)
        {
            _handshake?.TrySetResult(false);
            _handshake = null;
            try { _pipeWriter?.Dispose(); } catch (Exception) { }
            try { _pipe?.Dispose(); } catch (Exception) { }
            _pipeWriter = null;
            _pipe = null;
            string[] pending;
            lock (_stateLock)
            {
                pending = _pending.ToArray();
                _pending.Clear();
            }
            foreach (string id in pending)
            {
                await WriteClientAsync(Error(JsonNode.Parse(id), -32000, reason));
            }
        }

        private void Track(JsonNode id, bool add)
        {
            string key = id.ToJsonString();
            lock (_stateLock)
            {
                if (add) _pending.Add(key);
                else _pending.Remove(key);
            }
        }

        private static string? IdString(JsonNode id) => id is JsonValue v && v.TryGetValue<string>(out string? s) ? s : null;

        // ------------------------------------------------------------ 書き出し

        private async Task WritePipeAsync(string line)
        {
            var writer = _pipeWriter;
            if (writer is null) return;
            try
            {
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                Log($"pipe write failed: {ex.GetType().Name}");
                await DisconnectAsync("本体へ送れませんでした");
            }
        }

        private async Task WriteClientAsync(string line)
        {
            await _stdoutGate.WaitAsync();
            try
            {
                await _stdout.WriteLineAsync(line);
            }
            finally
            {
                _stdoutGate.Release();
            }
        }

        private Task WriteClientAsync(JsonObject message) => WriteClientAsync(message.ToJsonString());

        private static JsonObject Result(JsonNode? id, JsonNode result) => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };

        private static JsonObject Error(JsonNode? id, int code, string message) => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };

        private void Log(string message)
        {
            if (!_verbose) return;
            try
            {
                Console.Error.WriteLine($"[NicoKaraPrep --mcp {DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch (Exception)
            {
                // 標準エラーへ書けなくても続ける
            }
        }
    }
}
