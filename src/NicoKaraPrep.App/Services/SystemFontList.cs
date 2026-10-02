using Microsoft.Graphics.Canvas.Text;

namespace NicoKaraPrep.App.Services;

/// <summary>システムにインストールされたフォントの一覧（Win2D・DirectWrite）。</summary>
public static class SystemFontList
{
    /// <summary>システムにインストールされたフォントファミリー一覧。</summary>
    public static string[] GetFamilies() =>
        CanvasTextFormat.GetSystemFontFamilies(new[] { "ja-JP" });
}
