using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using NicoKaraPrep.App.Services.Mcp;

namespace NicoKaraPrep.App;

/// <summary>
/// 起動の入口（XAML が生成する Main の代わり。csproj の DISABLE_XAML_GENERATED_MAIN）。
/// ・<c>--mcp</c> 付きで起動されたときは、画面を出さずに MCP の橋渡し（標準入出力 ⇄ 起動中の本体の名前付きパイプ）として動く
/// 　（Claude Code などの MCP クライアントに「NicoKaraPrep.exe --mcp」を登録する。<see cref="McpBridge"/>）。
/// ・それ以外は 1 つのインスタンスだけを動かす。すでに起動していれば、引数（開くファイル）をそちらへ渡して終わる
/// 　（同じ場所の exe ごと。別のフォルダのビルドは別のインスタンスとして動く。<see cref="McpInfo.InstanceKey"/>）。
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (McpBridge.IsBridgeInvocation(args)) return McpBridge.Run(args);
        return RunApp();
    }

    /// <summary>
    /// 画面のアプリとして起動する（XAML が生成していた Main と同じ）。ほかのインスタンスが動いていれば、引数を渡して終わる。
    /// 橋渡しのときに画面の部品を読み込まないよう、Main とは別のメソッドにする。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return 0;
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    // ------------------------------------------------------------ 単一インスタンス

    /// <summary>
    /// 同じ exe のインスタンスがすでに動いていれば、この起動の引数をそちらへ渡して true を返す（この起動は終わる）。
    /// 最初のインスタンスなら、あとから渡される引数を受け取るようにして false を返す。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool RedirectToRunningInstance()
    {
        AppInstance main;
        AppActivationArguments activation;
        try
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            main = AppInstance.FindOrRegisterForKey(McpInfo.InstanceKey);
        }
        catch (Exception)
        {
            return false; // 仕組みが使えない環境では、今までどおり別々に起動する
        }
        if (main.IsCurrent)
        {
            main.Activated += OnRedirectedActivation;
            return false;
        }
        // 最初のインスタンスがウィンドウを前に出せるように（前に出す権利はユーザーが起動したこのプロセスにある）
        AllowSetForegroundWindow(AsfwAny);
        RedirectActivationTo(activation, main);
        return true;
    }

    /// <summary>あとから起動したインスタンスの引数（開くファイル）を受け取る（UI スレッドではない）。</summary>
    private static void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        string? path = null;
        try
        {
            if (args.Kind == ExtendedActivationKind.Launch && args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
            {
                path = FindFileArgument(launch.Arguments);
            }
        }
        catch (Exception)
        {
            path = null;
        }
        App.OnActivatedFromAnotherInstance(path);
    }

    /// <summary>コマンドラインの文字列から、開くファイル（存在するファイルのパス）を探す。exe 自身のパスは除く。</summary>
    internal static string? FindFileArgument(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        string? exe = Environment.ProcessPath;
        foreach (string token in SplitCommandLine(commandLine))
        {
            if (token.StartsWith("--", StringComparison.Ordinal)) continue;
            if (exe is not null && string.Equals(Path.GetFullPath(token), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (File.Exists(token)) return Path.GetFullPath(token);
            }
            catch (Exception)
            {
                // パスとして扱えない引数
            }
        }
        return null;
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        if (argv == IntPtr.Zero) return Array.Empty<string>();
        try
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            }
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>
    /// 引数を最初のインスタンスへ渡す。Main の中ではまだ待てる仕組み（DispatcherQueue）が無いので、
    /// 別のスレッドで渡し終わるのをイベントで待つ（Windows App SDK の案内どおりの手順）。
    /// </summary>
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance target)
    {
        IntPtr done = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try
            {
                target.RedirectActivationToAsync(args).AsTask().Wait();
            }
            catch (Exception)
            {
                // 渡せなくても終わる（相手が終了中など）
            }
            SetEvent(done);
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, new[] { done }, out _);
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
