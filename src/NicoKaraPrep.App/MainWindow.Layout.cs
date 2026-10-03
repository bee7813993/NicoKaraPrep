using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Project;
using Windows.Foundation;

namespace NicoKaraPrep.App;

/// <summary>
/// 画面の大きさに合わせた並べ方。FHD（1920×1080）や、125%・150% 表示のような低く狭い画面でも、行リストが見えるようにする。
/// ・メディア再生: 上の段の横いっぱいか、右の列（右のパネルの上。行リストが縦いっぱいになる。高さは列の幅から決める）に置く。
///   表示メニューで選ぶ（既定の自動は、窓が低いとき（FHD の画面など）だけ右の列）。上の段の高さは設定の高さ（つまみで変える）まで使い、
///   行リスト（絵文字挿入ビューでは編集欄）の高さが残らないときは、残るまで縮める
/// ・右の列: 境のつまみで幅を変える（メディア再生を右に置くときと置かないときで別に覚える）。左が狭くなりすぎるときは狭める。
///   右のパネル（フォント・レイアウト、絵文字のパレット）は表示メニューで隠せる
/// ・行リスト: フォント・レイアウトの欄を行の中身に合わせる。狭いときは時間と横幅を 2 段にして、歌詞の欄を広く取る（<see cref="LineListLayout"/>）
/// ・ニコカラメーカー用の行設定: 1 行に収まらないときは、フォント・レイアウトを 2 段目に回す
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>メディア再生の高さの下限・上限 px。</summary>
    private const double MinPlayerHeight = 100;

    private const double MaxPlayerHeight = 1200;

    /// <summary>上の段のメディア再生を縮めてでも残す、行リスト（絵文字挿入ビューでは編集欄）の高さ px（行リストで 3 行ほど）。</summary>
    private const double MinFlexHeight = 150;

    /// <summary>右の列のメディア再生を縮めてでも残す、右のパネルの中身の高さ px（行リスト: フォント一覧が数行、絵文字挿入ビュー: パレットが 1 段）。</summary>
    private const double MinSideContentHeight = 200;

    private const double MinSidePaletteHeight = 280;

    /// <summary>右の列の既定の幅・最小の幅 px（右のパネルだけのとき）。</summary>
    private const double DefaultSidePanelWidth = 300;

    private const double MinSidePanelWidth = 220;

    /// <summary>右の列の既定の幅・最小の幅 px（メディア再生も置くとき。最小は再生の操作の 2 段目が並ぶ幅）。</summary>
    private const double DefaultSidePlayerWidth = 560;

    private const double MinSidePlayerWidth = 380;

    /// <summary>
    /// 右の列を広げても残す、左（行リスト・絵文字挿入ビュー）の幅 px（行リストを詰めた並べ方で、歌詞の欄に 340px ほど残る幅）。
    /// ウィンドウが狭いときは、右の列をこの分だけ狭める（右の列の最小の幅まで）。
    /// </summary>
    private const double MinMainColumnWidth = 880;

    /// <summary>音量のスライダーの幅 px（右の列に置くときは狭める）。</summary>
    private const double VolumeSliderWidth = 96;

    private const double VolumeSliderWidthNarrow = 72;

    /// <summary>
    /// 置き場所が自動のとき、メディア再生を右の列に置く窓の高さ・幅（中身の論理 px）。FHD（1920×1080）の画面の窓は高さが 1000 前後。
    /// 幅は、右の列を最小の幅にして行リストを詰めた並べ方にしても、歌詞の欄が 370px ほど残る幅から（FHD の 150% 表示の 1280 も含める）。
    /// </summary>
    private const double AutoRightBelowHeight = 1100;

    private const double AutoRightMinWidth = 1200;

    /// <summary>行リストの項目の左右の余白（選択の印・スクロールバーの分）px。</summary>
    private const double LineItemChromeWidth = 28;

    /// <summary>ふつうの並べ方で歌詞の欄にこれだけ残らないときは、行リストの列を詰める px。</summary>
    private const double MinLyricsWidth = 480;

    /// <summary>フォント・レイアウトの欄の中身の大きさ（テンプレートと同じ）: 色見本 2 つ（8＋2＋8）・すき間 4、2 つ目の名前の最大の幅、文字の大きさ。</summary>
    private const double FontSwatchesWidth = 18;

    private const double FontCellGap = 4;

    private const double FontLastNameMaxWidth = 72;

    private const double FontNameSize = 12;

    private const double LayoutNameSize = 11;

    /// <summary>行設定を 1 行に並べるときに、右の説明（自動の表示時刻など）に最低限残す幅 px（足りない分は … で切り、全文はツールチップ）。</summary>
    private const double MinN3InfoWidth = 40;

    private const string MediaPanelHeaderWide = "メディア再生（動画・音声ファイルをここにドロップで読み込み）";
    private const string MediaPanelHeaderNarrow = "メディア再生（ドロップで読み込み）";

    private bool _playerOnRight;
    private double _sideWidthAtDragStart;
    private bool _n3PanelWrapped;
    private TextBlock? _measureBlock;
    private readonly Dictionary<(string Text, double Size), double> _textWidths = new();

    private void InitializeAdaptiveLayout()
    {
        var s = ViewModel.Settings;
        PlayerHost.Height = Math.Clamp(s.PlayerHeightPx, MinPlayerHeight, MaxPlayerHeight);
        SidePanelMenuItem.IsChecked = s.SidePanelVisible;
        SyncPlayerPlacementMenu();
        ApplyPlayerPlacement(WantsPlayerOnRight());

        RootGrid.SizeChanged += (_, _) =>
        {
            FitPlayerPlacement();
            FitPlayerHeight();
        };
        MainArea.SizeChanged += (_, _) =>
        {
            FitSidePanelWidth();
            FitPlayerHeight();
        };
        LineList.SizeChanged += (_, _) =>
        {
            FitLineListColumns();
            FitPlayerHeight();
        };
        InsertEditor.SizeChanged += (_, _) => FitPlayerHeight();
        PlayerHost.SizeChanged += (_, e) =>
        {
            // 右の列では高さを幅から決めるので、幅が変わったら合わせ直す
            if (_playerOnRight && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 0.5) FitPlayerHeight();
        };
        MediaPanel.Expanding += (_, _) => FitPlayerHeightAfterLayout();
        N3LinePanel.SizeChanged += (_, _) => FitN3LinePanel();
        N3TimeGroup.SizeChanged += (_, _) => FitN3LinePanel();
        N3FontGroup.SizeChanged += (_, _) => FitN3LinePanel();
        ViewModel.LineFontsUpdated += (_, _) => FitLineListFontColumn();

        // 右の列の境のつまみ（右へ動かすと右の列が狭くなる）
        SideResizeGrip.DragStarted += (_, _) => _sideWidthAtDragStart = SideColumn.Width.Value;
        SideResizeGrip.Dragging += (_, dx) => SetSidePanelWidth(_sideWidthAtDragStart - dx, save: false);
        SideResizeGrip.DragCompleted += (_, _) => SetSidePanelWidth(SideColumn.Width.Value, save: true);
        SideResizeGrip.Stepped += (_, dx) => SetSidePanelWidth(SideColumn.Width.Value - dx, save: true);
        SideResizeGrip.ResetRequested += (_, _) => SetSidePanelWidth(_playerOnRight ? DefaultSidePlayerWidth : DefaultSidePanelWidth, save: true);
    }

    // ------------------------------------------------ メディア再生の置き場所と高さ

    /// <summary>表示 > メディア再生の置き場所（自動・上の段・右の列）。</summary>
    private void OnPlayerPlacementMenuClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.PlayerPlacement = ReferenceEquals(sender, PlayerPlacementTopItem) ? PlayerPlacementMode.Top
            : ReferenceEquals(sender, PlayerPlacementRightItem) ? PlayerPlacementMode.Right
            : PlayerPlacementMode.Auto;
        ViewModel.Settings.Save();
        SyncPlayerPlacementMenu();
        FitPlayerPlacement();
    }

    private void SyncPlayerPlacementMenu()
    {
        var mode = ViewModel.Settings.PlayerPlacement;
        PlayerPlacementAutoItem.IsChecked = mode == PlayerPlacementMode.Auto;
        PlayerPlacementTopItem.IsChecked = mode == PlayerPlacementMode.Top;
        PlayerPlacementRightItem.IsChecked = mode == PlayerPlacementMode.Right;
    }

    /// <summary>
    /// メディア再生を右の列に置くか。自動は、窓が低い（<see cref="AutoRightBelowHeight"/> 未満）ときだけ右の列
    /// （窓が狭くて右の列に置くと行リストの幅が残らないときは上の段）。窓の大きさが決まる前は上の段。
    /// </summary>
    private bool WantsPlayerOnRight() => ViewModel.Settings.PlayerPlacement switch
    {
        PlayerPlacementMode.Right => true,
        PlayerPlacementMode.Top => false,
        _ => RootGrid.ActualHeight > 0 && RootGrid.ActualHeight < AutoRightBelowHeight && RootGrid.ActualWidth >= AutoRightMinWidth,
    };

    /// <summary>メディア再生の置き場所を、表示メニューの選択と窓の大きさに合わせる（変わるときだけ置き直す）。</summary>
    private void FitPlayerPlacement()
    {
        bool right = WantsPlayerOnRight();
        if (right != _playerOnRight) ApplyPlayerPlacement(right);
    }

    /// <summary>
    /// メディア再生を、上の段の横いっぱい（right = false）か、右の列の上（right = true）に置く。
    /// 要素は同じ Grid（MainArea）の中で段と列を変えるだけ（動画の部品を別の親へ移さない）。
    /// 右に置くときは、行リスト・絵文字挿入ビューとつまみを上から下まで通し、再生の位置のスライダーを 1 段目の横いっぱいにする。
    /// </summary>
    private void ApplyPlayerPlacement(bool right)
    {
        _playerOnRight = right;
        Grid.SetColumn(MediaPanel, right ? 2 : 0);
        Grid.SetColumnSpan(MediaPanel, right ? 1 : 3);
        foreach (var element in new FrameworkElement[] { NormalView, InsertView, SideResizeGrip })
        {
            Grid.SetRow(element, right ? 0 : 1);
            Grid.SetRowSpan(element, right ? 2 : 1);
        }
        MediaPanel.Header = right ? MediaPanelHeaderNarrow : MediaPanelHeaderWide;
        PlayerResizeHandle.Visibility = right ? Visibility.Collapsed : Visibility.Visible;

        // 再生の操作: 右の列は狭いので、位置のスライダーを 1 段目に、ボタンなどを 2 段目に
        foreach (var child in PlayerBar.Children)
        {
            if (child is FrameworkElement element && !ReferenceEquals(element, PlayerSeekSlider)) Grid.SetRow(element, right ? 1 : 0);
        }
        Grid.SetColumn(PlayerSeekSlider, right ? 0 : 2);
        Grid.SetColumnSpan(PlayerSeekSlider, right ? PlayerBar.ColumnDefinitions.Count : 1);
        PlayerVolumeSlider.Width = right ? VolumeSliderWidthNarrow : VolumeSliderWidth;

        if (!right) PlayerHost.Height = Math.Clamp(ViewModel.Settings.PlayerHeightPx, MinPlayerHeight, MaxPlayerHeight);
        ApplySidePanelVisibility(ViewModel.ViewMode);
        FitPlayerHeightAfterLayout();
    }

    /// <summary>
    /// メディア再生の高さを合わせる。上の段: 設定の高さ（行リストに <see cref="MinFlexHeight"/> が残らないときは縮める）。
    /// 右の列: 字幕の画面の縦横比で列の幅いっぱい（右のパネルの中身に <see cref="MinSideContentHeight"/>・<see cref="MinSidePaletteHeight"/> が残らないときは縮める）。
    /// どちらも、縮めた分だけほかの欄が伸びるので、何度呼んでも同じ高さになる。
    /// </summary>
    private void FitPlayerHeight()
    {
        if (MediaPanel.Visibility != Visibility.Visible || !MediaPanel.IsExpanded) return;
        double target = _playerOnRight ? SidePlayerHeight() : StackedPlayerHeight();
        if (double.IsNaN(target)) return;
        if (Math.Abs(PlayerHost.Height - target) >= 0.5) PlayerHost.Height = target;
    }

    /// <summary>次の並べ直しのあとで <see cref="FitPlayerHeight"/> する（置き場所・ビューを変えたとき・メディア再生を開いたとき。大きさの変わらない欄からは知らせが来ないため）。</summary>
    private void FitPlayerHeightAfterLayout()
    {
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            RootGrid.LayoutUpdated -= handler;
            FitPlayerHeight();
        };
        RootGrid.LayoutUpdated += handler;
    }

    private double StackedPlayerHeight()
    {
        double cap = PlayerHeightCap();
        if (double.IsNaN(cap)) return double.NaN;
        return Math.Min(Math.Clamp(ViewModel.Settings.PlayerHeightPx, MinPlayerHeight, MaxPlayerHeight), cap);
    }

    /// <summary>上の段のメディア再生で、行リスト（絵文字挿入ビューでは編集欄）に高さを残せる高さの上限 px（決められないときは NaN）。</summary>
    private double PlayerHeightCap()
    {
        (Grid? view, int flexRow) = ViewModel.ViewMode switch
        {
            MainViewMode.Lines => (NormalView, 1),
            MainViewMode.EmojiInsert => (InsertView, 2),
            _ => ((Grid?)null, -1),
        };
        if (view is null || PlayerHost.ActualHeight <= 0 || MainArea.ActualHeight <= 0) return double.NaN;
        // メディア再生の動画と、行リスト（編集欄）は、メインの高さから見出し・再生の操作・ビューの決まった段を除いた残りを分け合う。
        // 行リスト・編集欄そのものの大きさは使わない（絵文字挿入ビューの編集欄は、大きさが何回かに分けて遅れて変わるため、
        // 使うと動画の高さが伸び縮みを繰り返す）
        double chrome = MediaPanel.ActualHeight - PlayerHost.ActualHeight;
        double shared = MainArea.ActualHeight - chrome - FixedRowsHeight(view, flexRow);
        return Math.Clamp(shared - MinFlexHeight, MinPlayerHeight, MaxPlayerHeight);
    }

    /// <summary>Grid の、伸び縮みする段（flexRow）を除いた段の高さに、段のすき間・余白・枠を足したもの。</summary>
    private static double FixedRowsHeight(Grid grid, int flexRow)
    {
        double height = grid.Padding.Top + grid.Padding.Bottom + grid.BorderThickness.Top + grid.BorderThickness.Bottom
            + grid.RowSpacing * Math.Max(0, grid.RowDefinitions.Count - 1);
        for (int i = 0; i < grid.RowDefinitions.Count; i++)
        {
            if (i != flexRow) height += grid.RowDefinitions[i].ActualHeight;
        }
        return height;
    }

    private double SidePlayerHeight()
    {
        double width = PlayerHost.ActualWidth;
        if (width <= 0 || MainArea.ActualHeight <= 0 || PlayerHost.ActualHeight <= 0) return double.NaN;
        double aspect = SubtitlePreview.Model is { } model ? (double)model.ScreenHeight / model.ScreenWidth : 9.0 / 16;
        // 見出し・余白・再生の操作の分を除いた残りから、右のパネルの中身の分を残す
        double chrome = MediaPanel.ActualHeight - PlayerHost.ActualHeight;
        double content = LineSideHost.Visibility == Visibility.Visible ? MinSideContentHeight
            : EmojiSidePanel.Visibility == Visibility.Visible ? MinSidePaletteHeight
            : 0;
        double room = MainArea.ActualHeight - chrome - content;
        return Math.Clamp(Math.Min(width * aspect, room), MinPlayerHeight, MaxPlayerHeight);
    }

    private void OnPlayerResizeDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double current = double.IsNaN(PlayerHost.Height) ? PlayerHost.ActualHeight : PlayerHost.Height;
        double cap = PlayerHeightCap();
        double max = double.IsNaN(cap) ? MaxPlayerHeight : cap;
        double height = Math.Clamp(current + e.Delta.Translation.Y, MinPlayerHeight, max);
        // 設定の高さも一緒に変える（行リストの大きさが変わると FitPlayerHeight が設定の高さに合わせ直すので、
        // 変えないとドラッグした高さがすぐ戻される）。設定の保存は離したとき
        ViewModel.Settings.PlayerHeightPx = height;
        PlayerHost.Height = height;
    }

    private void OnPlayerResizeCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        ViewModel.Settings.PlayerHeightPx = PlayerHost.Height;
        ViewModel.Settings.Save();
    }

    // ------------------------------------------------ 右の列

    /// <summary>表示 > 右のパネルを表示。</summary>
    private void OnSidePanelMenuClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.SidePanelVisible = SidePanelMenuItem.IsChecked;
        ViewModel.Settings.Save();
        ApplySidePanelVisibility(ViewModel.ViewMode);
        FitPlayerHeightAfterLayout();
    }

    /// <summary>
    /// 右のパネル（行リストではフォント・レイアウト、絵文字挿入ビューでは絵文字のパレット）を、ビューと表示メニューの選択に合わせて出す・隠す。
    /// 右の列（とつまみ）は、右のパネルかメディア再生を置くときだけ出す。
    /// </summary>
    private void ApplySidePanelVisibility(MainViewMode mode)
    {
        bool content = ViewModel.Settings.SidePanelVisible;
        LineSideHost.Visibility = content && mode == MainViewMode.Lines ? Visibility.Visible : Visibility.Collapsed;
        EmojiSidePanel.Visibility = content && mode == MainViewMode.EmojiInsert ? Visibility.Visible : Visibility.Collapsed;
        bool column = content || _playerOnRight;
        SideResizeGrip.Visibility = column ? Visibility.Visible : Visibility.Collapsed;
        if (column)
        {
            FitSidePanelWidth();
        }
        else
        {
            SideColumn.Width = new GridLength(0);
        }
    }

    /// <summary>右の列の幅の設定（メディア再生を右に置くときと置かないときで別に覚える）。</summary>
    private double SideWidthSetting
    {
        get => _playerOnRight ? ViewModel.Settings.SidePlayerWidthPx : ViewModel.Settings.SidePanelWidthPx;
        set
        {
            if (_playerOnRight)
            {
                ViewModel.Settings.SidePlayerWidthPx = value;
            }
            else
            {
                ViewModel.Settings.SidePanelWidthPx = value;
            }
        }
    }

    /// <summary>右の列の幅を設定の幅にする（左が狭くなりすぎるときは狭める）。</summary>
    private void FitSidePanelWidth()
    {
        if (SideResizeGrip.Visibility != Visibility.Visible) return;
        double width = ClampSidePanelWidth(SideWidthSetting);
        if (Math.Abs(SideColumn.Width.Value - width) >= 0.5) SideColumn.Width = new GridLength(width);
    }

    /// <summary>右の列の幅を、最小の幅から、左に <see cref="MinMainColumnWidth"/> が残る幅までに収める。</summary>
    private double ClampSidePanelWidth(double width)
    {
        double min = _playerOnRight ? MinSidePlayerWidth : MinSidePanelWidth;
        double max = MainArea.ActualWidth > 0
            ? MainArea.ActualWidth - SideResizeGrip.ActualWidth - MinMainColumnWidth
            : 1600;
        return Math.Clamp(width, min, Math.Max(min, max));
    }

    /// <summary>つまみで右の列の幅を変える。save なら設定に保存する。</summary>
    private void SetSidePanelWidth(double width, bool save)
    {
        width = ClampSidePanelWidth(width);
        SideColumn.Width = new GridLength(width);
        if (!save) return;
        SideWidthSetting = width;
        ViewModel.Settings.Save();
    }

    // ------------------------------------------------ 行リストの列・行設定の段

    /// <summary>ふつうの並べ方で歌詞の欄に <see cref="MinLyricsWidth"/> が残らないときは、行リストの列を詰めた並べ方にする。</summary>
    private void FitLineListColumns()
    {
        if (LineList.ActualWidth <= 0) return;
        var layout = LineListLayout.Current;
        layout.Compact = LineList.ActualWidth - LineItemChromeWidth - layout.FixedWidth(compact: false) < MinLyricsWidth;
    }

    /// <summary>
    /// 行リストのフォント・レイアウトの欄の幅を、すべての行の中身（フォント設定の名前・レイアウト名）が入る幅にする
    /// （行のフォント設定・レイアウトを作り直すたびに呼ぶ）。
    /// </summary>
    private void FitLineListFontColumn()
    {
        double width = 0;
        foreach (var line in ViewModel.Lines) width = Math.Max(width, FontCellWidth(line));
        LineListLayout.Current.FontContentWidth = width;
        FitLineListColumns();
    }

    /// <summary>行のフォント・レイアウトの欄の中身の幅 px（テンプレートの並べ方と同じに測る）。</summary>
    private double FontCellWidth(LineViewModel line)
    {
        double width = 0;
        var font = line.AppliedFont;
        if (font.IsVisible)
        {
            // [色見本][名前]（途中で切り替わる行は [→][色見本][2 つ目の名前（72px まで）] を足す）
            width = FontSwatchesWidth + FontCellGap + TextWidth(font.First.Name, FontNameSize);
            if (font.HasLast)
            {
                width += FontCellGap + TextWidth(font.Arrow, FontNameSize) + FontCellGap + FontSwatchesWidth + FontCellGap
                    + Math.Min(FontLastNameMaxWidth, TextWidth(font.Last.Name, FontNameSize));
            }
        }
        if (line.HasLayoutText) width = Math.Max(width, TextWidth(line.LayoutText, LayoutNameSize));
        return width;
    }

    /// <summary>行リストと同じ書体で、文字の幅を測る（同じ文字と大きさは覚えておく）。</summary>
    private double TextWidth(string text, double size)
    {
        if (text.Length == 0) return 0;
        if (_textWidths.TryGetValue((text, size), out double width)) return width;
        _measureBlock ??= new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.Resources["ContentControlThemeFontFamily"],
        };
        _measureBlock.FontSize = size;
        _measureBlock.Text = text;
        _measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        width = Math.Ceiling(_measureBlock.DesiredSize.Width);
        if (width <= 0) width = text.Length * size; // 測れなかったときは全角の幅で見積もる
        _textWidths[(text, size)] = width;
        return width;
    }

    /// <summary>
    /// ニコカラメーカー用の行設定を、1 行に収まれば [表示時刻][フォント・レイアウト][説明] の 1 行に、
    /// 収まらなければ [表示時刻][説明] / [フォント・レイアウト] の 2 段に並べる。
    /// </summary>
    private void FitN3LinePanel()
    {
        double available = N3LinePanel.ActualWidth - N3LinePanel.Padding.Left - N3LinePanel.Padding.Right;
        if (available <= 0) return;
        double need = NaturalWidth(N3TimeGroup) + NaturalWidth(N3FontGroup) + N3LinePanel.ColumnSpacing * 2 + MinN3InfoWidth;
        bool wrap = need > available;
        if (wrap == _n3PanelWrapped) return;
        _n3PanelWrapped = wrap;
        Grid.SetRow(N3FontGroup, wrap ? 1 : 0);
        Grid.SetColumn(N3FontGroup, wrap ? 0 : 1);
        Grid.SetColumnSpan(N3FontGroup, wrap ? 3 : 1);
        Grid.SetColumn(N3LineInfo, wrap ? 1 : 2);
        Grid.SetColumnSpan(N3LineInfo, wrap ? 2 : 1);
    }

    /// <summary>
    /// 横に並べた StackPanel の、中身を縮めずに並べたときの幅（子は横に無制限の幅で測られるので、子の大きさの合計）。
    /// StackPanel 自身の大きさは置かれた欄の幅で頭打ちになるため、並べ方を決めるのには使えない。
    /// </summary>
    private static double NaturalWidth(StackPanel panel)
    {
        double width = 0;
        int count = 0;
        foreach (var child in panel.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            width += child.DesiredSize.Width;
            count++;
        }
        return width + Math.Max(0, count - 1) * panel.Spacing + panel.Padding.Left + panel.Padding.Right;
    }
}
