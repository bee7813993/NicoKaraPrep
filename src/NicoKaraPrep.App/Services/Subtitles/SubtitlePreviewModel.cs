using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 字幕のプレビューの 1 行。BeginMs〜EndMs（タグの時刻の基準の ms）のあいだ、タブ Tab のページ Page の下から Row 段目に、
/// レイアウト Layout で出す。RowsInPage はページの段の数、LinesInPage はページの行の数（1 行のページは上の段に上がることがあるので段の数と違う）。
/// Wipe はワイプのまとまり。
/// </summary>
public sealed record PreviewLine(
    LineRenderSource Source,
    int BeginMs,
    int EndMs,
    int Tab,
    int Page,
    int Row,
    int RowsInPage,
    int LinesInPage,
    N3LayoutSettings Layout,
    IReadOnlyList<N3WipeTimeline.Group> Wipe);

/// <summary>字幕のプレビューの材料（全タブの行と、字幕の画面の大きさ）。</summary>
public sealed class SubtitlePreviewModel
{
    public SubtitlePreviewModel(int screenWidth, int screenHeight, List<PreviewLine> lines)
    {
        ScreenWidth = Math.Max(1, screenWidth);
        ScreenHeight = Math.Max(1, screenHeight);
        Lines = lines;
        Pages = lines.GroupBy(l => (l.Tab, l.Page)).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>字幕の画面の幅・高さ px（書き出すプロジェクトの画面の大きさ）。</summary>
    public int ScreenWidth { get; }

    public int ScreenHeight { get; }

    public IReadOnlyList<PreviewLine> Lines { get; }

    /// <summary>ページごとの行（スマート水平配置で、ページのいちばん長い行を見るため）。</summary>
    public IReadOnlyDictionary<(int Tab, int Page), List<PreviewLine>> Pages { get; }
}
