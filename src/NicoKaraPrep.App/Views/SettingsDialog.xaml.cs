using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.App.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (string family in SystemFontList.GetFamilies().OrderBy(f => f))
        {
            FontFamilyBox.Items.Add(family);
        }

        PageModeBox.SelectedIndex = settings.PageMode == PageSplitMode.EmptyLine ? 0 : 1;
        FixedLineCountBox.Value = settings.FixedLineCount;
        LeadBox.Value = settings.DisplayLeadSeconds;
        TailBox.Value = settings.DisplayTailSeconds;
        AlignBox.SelectedIndex = settings.CollisionAlignFromTop ? 0 : 1;
        ThresholdBox.Value = settings.CollisionErrorThresholdSeconds;
        ScreenWidthBox.Value = settings.ScreenWidthPx;
        FontFamilyBox.Text = settings.FontFamily;
        FontSizeBox.Value = settings.FontSizePx;
        EdgeSizeBox.Value = settings.EdgeSizePx;
        BoldBox.IsChecked = settings.FontBold;
        EmojiLeadBox.Value = settings.EmojiLeadSeconds;
        EmojiModeBox.SelectedIndex = settings.EmojiTagPerEmoji ? 0 : 1;
        PlaceholderBox.Text = settings.PlaceholderChar;
        SeekSecondsBox.Value = settings.SeekSeconds;
        McpBox.IsChecked = settings.McpEnabled;
        // MCP の登録のしかた（ストア版は実行エイリアス、それ以外は今の exe のパスで作る）
        McpCommandBox.Text = Services.Mcp.McpInfo.ClaudeCodeCommand;
        McpDesktopBox.Text = Services.Mcp.McpInfo.ClaudeDesktopEntry;

        PrimaryButtonClick += (_, _) => ApplyToSettings();
    }

    private void ApplyToSettings()
    {
        _settings.PageMode = PageModeBox.SelectedIndex == 1 ? PageSplitMode.FixedLineCount : PageSplitMode.EmptyLine;
        _settings.FixedLineCount = ToInt(FixedLineCountBox.Value, 2);
        _settings.DisplayLeadSeconds = ToDouble(LeadBox.Value, 1.5);
        _settings.DisplayTailSeconds = ToDouble(TailBox.Value, 0.5);
        _settings.CollisionAlignFromTop = AlignBox.SelectedIndex == 0;
        _settings.CollisionErrorThresholdSeconds = ToDouble(ThresholdBox.Value, 1.0);
        _settings.ScreenWidthPx = ToInt(ScreenWidthBox.Value, 1920);
        if (!string.IsNullOrWhiteSpace(FontFamilyBox.Text)) _settings.FontFamily = FontFamilyBox.Text;
        _settings.FontSizePx = ToDouble(FontSizeBox.Value, 80);
        _settings.EdgeSizePx = ToDouble(EdgeSizeBox.Value, 0);
        _settings.FontBold = BoldBox.IsChecked == true;
        _settings.EmojiLeadSeconds = ToDouble(EmojiLeadBox.Value, 2.0);
        if (_settings.EmojiLeadSeconds > 0) _settings.EmojiLeadResumeSeconds = _settings.EmojiLeadSeconds;
        _settings.EmojiTagPerEmoji = EmojiModeBox.SelectedIndex == 0;
        _settings.PlaceholderChar = PlaceholderBox.Text.Trim();
        _settings.SeekSeconds = ToDouble(SeekSecondsBox.Value, 3.0);
        _settings.McpEnabled = McpBox.IsChecked == true;
        _settings.Save();
    }

    private void OnCopyMcpCommandClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => CopyText(McpCommandBox.Text, McpCommandCopyButton);

    private void OnCopyMcpDesktopClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => CopyText(McpDesktopBox.Text, McpDesktopCopyButton);

    /// <summary>クリップボードへ写し、ボタンの文字で知らせる。</summary>
    private static void CopyText(string text, Button button)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            button.Content = "コピーしました";
        }
        catch (Exception)
        {
            button.Content = "コピーできません";
        }
    }

    private static int ToInt(double v, int fallback) => double.IsNaN(v) ? fallback : (int)Math.Round(v);

    private static double ToDouble(double v, double fallback) => double.IsNaN(v) ? fallback : v;
}
