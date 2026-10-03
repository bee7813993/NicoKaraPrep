using System.IO.Pipes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// 本体の中の MCP サーバー。名前付きパイプ（<see cref="McpInfo.PipeName"/>）で待ち受け、接続ごとに MCP のセッションを 1 つ動かす。
/// 道具の処理はすべて UI スレッドで 1 件ずつ行う（<see cref="MainWindow"/> の McpInvokeAsync）。
/// 接続できるのは同じユーザーのプロセスだけ（PipeOptions.CurrentUserOnly）。
/// </summary>
internal sealed class McpHost : IDisposable
{
    private readonly MainWindow _window;
    private CancellationTokenSource? _cts;
    private int _clients;

    public McpHost(MainWindow window)
    {
        _window = window;
    }

    /// <summary>接続しているクライアントの数が変わった（UI スレッドではない）。</summary>
    public event Action<int>? ClientCountChanged;

    /// <summary>接続しているクライアントの数。</summary>
    public int ClientCount => Volatile.Read(ref _clients);

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>待ち受けを始める。</summary>
    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>待ち受けをやめ、つながっているセッションを閉じる。</summary>
    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
            // 止めるときの失敗は無視
        }
        cts.Dispose();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    McpInfo.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception)
            {
                // 作れないとき（名前の衝突など）は少し待ってやり直す
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (Exception)
            {
                pipe.Dispose();
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            _ = ServeAsync(pipe, ct);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        ClientCountChanged?.Invoke(Interlocked.Increment(ref _clients));
        try
        {
            var options = CreateOptions();
            var transport = new StreamServerTransport(pipe, pipe, McpInfo.ServerName);
            await using var server = McpServer.Create(transport, options);
            await server.RunAsync(ct);
        }
        catch (Exception)
        {
            // クライアントが切った・止めた
        }
        finally
        {
            try { pipe.Dispose(); } catch (Exception) { }
            ClientCountChanged?.Invoke(Interlocked.Decrement(ref _clients));
        }
    }

    /// <summary>セッションの設定（サーバーの名前・説明と道具の一覧）。</summary>
    private McpServerOptions CreateOptions()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = McpInfo.ServerName, Version = McpInfo.Version },
            ServerInstructions = McpInfo.Instructions,
            ToolCollection = new McpServerPrimitiveCollection<McpServerTool>(),
        };
        foreach (var tool in McpToolCatalog.Create(_window))
        {
            options.ToolCollection.Add(tool);
        }
        return options;
    }
}
