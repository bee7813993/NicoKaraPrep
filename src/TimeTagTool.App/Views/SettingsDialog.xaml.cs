using Microsoft.UI.Xaml.Controls;
using TimeTagTool.App.Services;
using TimeTagTool.Core.Model;
using TimeTagTool.Core.Project;

namespace TimeTagTool.App.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (string family in DirectWriteTextMeasurer.GetSystemFontFamilies().OrderBy(f => f))
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
        BoldBox.IsChecked = settings.FontBold;
        MarginBox.Value = settings.SideMarginPercent;
        EmojiLeadBox.Value = settings.EmojiLeadSeconds;
        EmojiModeBox.SelectedIndex = settings.EmojiTagPerEmoji ? 0 : 1;
        PlaceholderBox.Text = settings.PlaceholderChar;
        SeekSecondsBox.Value = settings.SeekSeconds;

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
        _settings.FontBold = BoldBox.IsChecked == true;
        _settings.SideMarginPercent = ToDouble(MarginBox.Value, 5.0);
        _settings.EmojiLeadSeconds = ToDouble(EmojiLeadBox.Value, 2.0);
        _settings.EmojiTagPerEmoji = EmojiModeBox.SelectedIndex == 0;
        _settings.PlaceholderChar = PlaceholderBox.Text.Trim();
        _settings.SeekSeconds = ToDouble(SeekSecondsBox.Value, 3.0);
        _settings.Save();
    }

    private static int ToInt(double v, int fallback) => double.IsNaN(v) ? fallback : (int)Math.Round(v);

    private static double ToDouble(double v, double fallback) => double.IsNaN(v) ? fallback : v;
}
