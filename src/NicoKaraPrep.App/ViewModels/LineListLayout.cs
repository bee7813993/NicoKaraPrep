using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 行リストの列の並べ方（全行で共有する）。列: 選択 / 済・行番号 / 開始・終了時間 / 印（♪・横幅の判定）/ 横幅 / 手動指定 / フォント・レイアウト / 歌詞。
/// フォント・レイアウトの欄は、行の中身（フォント設定の名前・レイアウト名）がちょうど入る幅にする（<see cref="FontContentWidth"/>）。
/// 行リストが狭いとき（FHD の画面で動画を右の列に置いたときなど）は詰めた並べ方にして、歌詞の欄を広く取る:
/// 開始・終了時間を上下 2 段に、横幅（px と %）を 2 段に、フォント・レイアウトの欄を狭く。
/// 行のテンプレートは <c>{x:Bind vm:LineListLayout.Current.…, Mode=OneWay}</c> で参照する。
/// </summary>
public sealed partial class LineListLayout : ObservableObject
{
    /// <summary>列のすき間 px（テンプレートの ColumnSpacing と同じ）。</summary>
    public const double ColumnSpacing = 6;

    /// <summary>決まった幅の列（選択 24・済と行番号 36・印 18・手動指定 14）の合計 px。</summary>
    private const double SmallColumnsWidth = 24 + 36 + 18 + 14;

    /// <summary>列の数（すき間は列の数 − 1）。</summary>
    private const int ColumnCount = 8;

    /// <summary>時間の欄 1 つ分の幅 px（Consolas 14px の「00:00:00」が入る幅。テンプレートの TextBlock の Width と同じ）。</summary>
    private const double TimeCellWidth = 64;

    /// <summary>フォント・レイアウトの欄の最小・最大の幅 px（ふつう・詰めたとき）。中身がそれより長ければ … で切る。</summary>
    private const double MinFontColumnWidth = 60;

    private const double MaxFontColumnWidth = 220;

    private const double MaxFontColumnWidthCompact = 160;

    public static LineListLayout Current { get; } = new();

    /// <summary>詰めた並べ方か。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeOrientation))]
    [NotifyPropertyChangedFor(nameof(TimeSpacing))]
    [NotifyPropertyChangedFor(nameof(TimeColumnWidth))]
    [NotifyPropertyChangedFor(nameof(WidthColumnWidth))]
    [NotifyPropertyChangedFor(nameof(FontColumnWidth))]
    private bool compact;

    /// <summary>
    /// フォント・レイアウトの欄の中身がちょうど入る幅 px（すべての行のうち最も長いもの）。出すものが無ければ 0（欄を閉じる）。
    /// 行のフォント設定・レイアウトを作り直すたびに測り直す。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontColumnWidth))]
    private double fontContentWidth = MaxFontColumnWidth;

    /// <summary>開始・終了時間の並べ方（ふつうは横、詰めると上下）。</summary>
    public Orientation TimeOrientation => Compact ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>開始と終了の時間のすき間 px。</summary>
    public double TimeSpacing => Compact ? 0 : ColumnSpacing;

    /// <summary>開始・終了時間の欄の幅。</summary>
    public GridLength TimeColumnWidth => new(TimeWidth(Compact));

    /// <summary>横幅（px と %）の欄の幅（詰めると折り返して 2 段になる）。</summary>
    public GridLength WidthColumnWidth => new(WidthWidth(Compact));

    /// <summary>フォント・レイアウトの欄の幅。</summary>
    public GridLength FontColumnWidth => new(FontWidth(Compact));

    /// <summary>歌詞の欄より左の列（すき間を含む）の合計 px。</summary>
    public double FixedWidth(bool compact) =>
        SmallColumnsWidth + TimeWidth(compact) + WidthWidth(compact) + FontWidth(compact) + ColumnSpacing * (ColumnCount - 1);

    private static double TimeWidth(bool compact) => compact ? TimeCellWidth : TimeCellWidth * 2 + ColumnSpacing;

    /// <summary>「1838px 101%」が 1 行に入る幅・「1838px」が入る幅（12px）。</summary>
    private static double WidthWidth(bool compact) => compact ? 44 : 78;

    private double FontWidth(bool compact) =>
        FontContentWidth <= 0 ? 0 : Math.Clamp(Math.Ceiling(FontContentWidth), MinFontColumnWidth, compact ? MaxFontColumnWidthCompact : MaxFontColumnWidth);
}
