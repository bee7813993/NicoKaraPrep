using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 行リストの列の並べ方（全行で共有する）。行リストが狭いとき（FHD の 125%・150% 表示など）は詰めた並べ方にして、歌詞の欄を広く取る:
/// 開始・終了時間を上下 2 段に、横幅（px と %）を 2 段に、フォント・レイアウトの欄を狭く。
/// 行のテンプレートは <c>{x:Bind vm:LineListLayout.Current.…, Mode=OneWay}</c> で参照する。
/// </summary>
public sealed partial class LineListLayout : ObservableObject
{
    /// <summary>時間の欄 1 つ分の幅 px。</summary>
    private const double TimeCellWidth = 76;

    /// <summary>開始と終了の時間を横に並べるときのすき間 px（列のすき間と同じ）。</summary>
    private const double TimeSpacingWide = 8;

    public static LineListLayout Current { get; } = new();

    /// <summary>詰めた並べ方か。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeOrientation))]
    [NotifyPropertyChangedFor(nameof(TimeSpacing))]
    [NotifyPropertyChangedFor(nameof(TimeColumnWidth))]
    [NotifyPropertyChangedFor(nameof(WidthColumnWidth))]
    [NotifyPropertyChangedFor(nameof(FontColumnWidth))]
    private bool compact;

    /// <summary>開始・終了時間の並べ方（ふつうは横、詰めると上下）。</summary>
    public Orientation TimeOrientation => Compact ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>開始と終了の時間のすき間 px。</summary>
    public double TimeSpacing => Compact ? 0 : TimeSpacingWide;

    /// <summary>開始・終了時間の欄の幅。</summary>
    public GridLength TimeColumnWidth => new(Compact ? TimeCellWidth : TimeCellWidth * 2 + TimeSpacingWide);

    /// <summary>横幅（px と %）の欄の幅（詰めると折り返して 2 段になる）。</summary>
    public GridLength WidthColumnWidth => new(Compact ? 52 : 92);

    /// <summary>フォント・レイアウトの欄の幅。</summary>
    public GridLength FontColumnWidth => new(Compact ? 160 : 220);
}
