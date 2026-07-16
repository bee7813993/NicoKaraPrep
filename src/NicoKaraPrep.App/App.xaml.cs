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
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NicoKaraPrep");
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
        MainWindow.Activate();
    }
}
