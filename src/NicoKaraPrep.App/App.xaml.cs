using Microsoft.UI.Xaml;

namespace NicoKaraPrep.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // 未処理例外をクラッシュログへ（%APPDATA%\NicoKaraPrep\crash.log）
        UnhandledException += (_, e) =>
        {
            try
            {
                string dir = Core.Project.AppSettings.DataFolder;
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Message}\n{e.Exception}\n\n");
            }
            catch
            {
                // ログ失敗は無視
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        // 検証用: --debug-hidden ではウィンドウを出さない（MCP の道具の確認を、画面とキーボードを使わずに行うため。
        // あとから同じ exe が起動して引数を渡されても出さない）
        StartedHidden = Environment.GetCommandLineArgs().Contains("--debug-hidden");
        if (!StartedHidden) MainWindow.Activate();
    }

    /// <summary>--debug-hidden（ウィンドウを出さない検証用の起動）で起動したか。</summary>
    public static bool StartedHidden { get; private set; }

    /// <summary>
    /// あとから起動したインスタンス（exe へのドロップ・ファイルの関連付け・2 回目の起動）の引数を受け取った（UI スレッドではない。Program.cs）。
    /// 画面を前に出し、ファイルがあれば開く。
    /// </summary>
    public static void OnActivatedFromAnotherInstance(string? path)
    {
        if (MainWindow is not MainWindow window) return;
        window.DispatcherQueue.TryEnqueue(() => window.OpenFromAnotherInstance(path));
    }
}
