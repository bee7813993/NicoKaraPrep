using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using TimeTagTool.Core.Validation;
using Windows.UI.Text;

namespace TimeTagTool.App.Services;

/// <summary>Win2D（DirectWrite）によるテキスト幅の実測。</summary>
public sealed class DirectWriteTextMeasurer : ITextMeasurer
{
    public double MeasureWidth(string text, string fontFamily, double fontSize, bool bold, bool italic)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = fontFamily,
            FontSize = (float)fontSize,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), text, format, float.MaxValue, float.MaxValue);
        return layout.LayoutBounds.Width;
    }

    /// <summary>システムにインストールされたフォントファミリー一覧。</summary>
    public static string[] GetSystemFontFamilies() =>
        CanvasTextFormat.GetSystemFontFamilies(new[] { "ja-JP" });
}
