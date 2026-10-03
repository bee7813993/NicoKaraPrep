using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>レイアウトの画面の一覧の 1 件（Edited: NicoKaraPrep で編集したもの、Added: ベースに無い名前で足したもの）。</summary>
public sealed record LayoutPickItem(string Name, string Display, bool Edited, bool Added);

/// <summary>
/// 行リストのときの右のパネル。「フォント」: 押したフォント設定を、行リストで選んだ文字（文字を選んでいなければ選んだ行）に指定する。
/// 「レイアウト」: ニコカラメーカー3 のレイアウト設定を選んで編集する（編集はアプリ共通で保存。見本は選んだ行のページを、選んだレイアウトで並べる）。
/// </summary>
public sealed partial class LineSidePanel : UserControl
{
    private static readonly string[] AlignmentNames = { "左寄せ", "中央", "右寄せ" };

    private MainViewModel? _vm;
    private bool _loading;
    private bool _saving;
    private string? _layoutName;
    private LineViewModel? _line;
    private string _fontsKey = "";

    public LineSidePanel()
    {
        InitializeComponent();
    }

    /// <summary>フォント設定を押した（null は「自動に戻す」）。</summary>
    public event EventHandler<string?>? FontPicked;

    /// <summary>「選んだ行に適用」を押した（レイアウト設定名）。</summary>
    public event EventHandler<string>? LayoutApplyRequested;

    /// <summary>メイン画面の ViewModel とつなぐ（起動時に 1 回）。</summary>
    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        vm.LayoutsChanged += (_, _) =>
        {
            if (_saving) UpdateLayoutState(); // 自分で編集したとき: 一覧の印と状態だけ
            else RefreshLayouts();
        };
        vm.PreviewModelChanged += (_, _) =>
        {
            RefreshFonts();
            RefreshLayoutPreview();
            if (IsShowTimePaneVisible) RefreshShowTimeSummary(); // 歌詞を直した・取り込んだ・自動調整した後の行数
        };
        RefreshFonts(force: true);
        RefreshLayouts();
    }

    /// <summary>フォントを押したときに指定する先の説明（「10 行目の選んだ 3 文字に指定します」など）。</summary>
    public void SetTarget(string text) => FontTargetText.Text = text;

    private void OnPaneChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool layout = ReferenceEquals(sender.SelectedItem, LayoutPaneItem);
        bool showTime = ReferenceEquals(sender.SelectedItem, ShowTimePaneItem);
        FontPane.Visibility = layout || showTime ? Visibility.Collapsed : Visibility.Visible;
        LayoutPane.Visibility = layout ? Visibility.Visible : Visibility.Collapsed;
        ShowTimePane.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
        if (layout)
        {
            SelectLayoutOfLine(_line);
            RefreshLayoutPreview();
        }
        if (showTime) RefreshShowTimePane();
    }

    // ------------------------------------------------------------ フォント

    /// <summary>フォントの一覧を作り直す（中身が同じなら作り直さない。スクロールの位置を保つため）。</summary>
    public void RefreshFonts(bool force = false)
    {
        if (_vm is null) return;
        var groups = _vm.BuildFontPickGroups(FontSearchBox.Text);
        string key = string.Join("|", groups.Select(g =>
            g.Key + ":" + string.Join(",", g.Select(i => $"{i.Name}/{RuntimeHelpers.GetHashCode(i.After!)}/{RuntimeHelpers.GetHashCode(i.Before!)}"))));
        if (!force && key == _fontsKey) return;
        _fontsKey = key;
        FontGroupsSource.Source = groups;
        FontPickList.ItemsSource = FontGroupsSource.View;
    }

    private void OnFontSearchChanged(object sender, TextChangedEventArgs e) => RefreshFonts(force: true);

    private void OnFontPickClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FontPickItem item) FontPicked?.Invoke(this, item.Name);
    }

    private void OnFontAutoClick(object sender, RoutedEventArgs e) => FontPicked?.Invoke(this, null);

    // ------------------------------------------------------------ レイアウト: 一覧

    /// <summary>レイアウトの一覧を作り直す（選んでいる名前は保つ）。</summary>
    public void RefreshLayouts()
    {
        if (_vm is null) return;
        var items = LayoutItems();
        _loading = true;
        try
        {
            LayoutPicker.ItemsSource = items;
            var selected = items.FirstOrDefault(i => i.Name == _layoutName) ?? items.FirstOrDefault();
            LayoutPicker.SelectedItem = selected;
            _layoutName = selected?.Name;
        }
        finally
        {
            _loading = false;
        }
        LoadLayoutFields();
        RefreshLayoutPreview();
    }

    private List<LayoutPickItem> LayoutItems()
    {
        var vm = _vm!;
        return vm.GetEffectiveLayouts().Select(l =>
        {
            bool edited = vm.FindEditedLayout(l.Name) is not null;
            bool added = edited && !vm.IsBaseLayout(l.Name);
            string mark = added ? "（追加）" : edited ? "（編集済み）" : "";
            return new LayoutPickItem(l.Name, $"{l.Name}{mark}　{l.LineCount} 行", edited, added);
        }).ToList();
    }

    /// <summary>行を選んだ: レイアウトの画面では、その行のページのレイアウトを選ぶ（見本もそのページ）。</summary>
    public void SelectLayoutOfLine(LineViewModel? line)
    {
        _line = line;
        if (LayoutPane.Visibility != Visibility.Visible) return;
        string name = line?.LayoutText.TrimStart('✎') ?? "";
        if (name.Length > 0 && name != _layoutName && LayoutPicker.ItemsSource is List<LayoutPickItem> items &&
            items.FirstOrDefault(i => i.Name == name) is { } item)
        {
            _layoutName = name;
            _loading = true;
            try
            {
                LayoutPicker.SelectedItem = item;
            }
            finally
            {
                _loading = false;
            }
            LoadLayoutFields();
        }
        RefreshLayoutPreview();
    }

    private void OnLayoutPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _layoutName = (LayoutPicker.SelectedItem as LayoutPickItem)?.Name;
        LoadLayoutFields();
        RefreshLayoutPreview();
    }

    private N3LayoutSettings? CurrentSettings() => _layoutName is null ? null : _vm?.GetEffectiveLayouts().FirstOrDefault(l => l.Name == _layoutName);

    /// <summary>一覧の印と、「元に戻す」ボタン・状態の説明を今の状態にする（編集したあと）。</summary>
    private void UpdateLayoutState()
    {
        if (_vm is null) return;
        var items = LayoutItems();
        var current = items.FirstOrDefault(i => i.Name == _layoutName);
        if (LayoutPicker.ItemsSource is List<LayoutPickItem> old && !old.SequenceEqual(items))
        {
            _loading = true;
            try
            {
                LayoutPicker.ItemsSource = items;
                LayoutPicker.SelectedItem = current;
            }
            finally
            {
                _loading = false;
            }
        }
        LayoutRemoveButton.Content = current?.Added == true ? "削除" : "元に戻す";
        LayoutRemoveButton.IsEnabled = current?.Edited == true;
        LayoutNameBox.IsEnabled = current?.Added == true;
        LayoutStateText.Text = current is null ? ""
            : current.Added ? "NicoKaraPrep で足したレイアウト設定です（書き出しで追加します）"
            : current.Edited ? "編集済み（書き出しで、ベースの同じ名前のレイアウト設定に上書きします。「元に戻す」でベースの値に戻ります）"
            : "ベースの n3proj（無ければ書き出しの既定）の値です。値を変えると編集済みになります";
    }

    // ------------------------------------------------------------ レイアウト: 値の欄

    private void LoadLayoutFields()
    {
        var s = CurrentSettings();
        _loading = true;
        try
        {
            if (s is null)
            {
                LayoutNameBox.Text = "";
                AlignmentRows.Children.Clear();
                return;
            }
            LayoutNameBox.Text = s.Name;
            VerticalBox.SelectedIndex = Math.Clamp(s.VerticalAlignment, 0, 2);
            VerticalMarginLabel.Text = s.VerticalAlignment switch { 0 => "上余白 px", 2 => "下余白 px", _ => "上下余白 px" };
            VerticalMarginBox.Value = s.VerticalMarginPx;
            HorizontalMarginBox.Value = s.HorizontalMarginPx;
            LineSpaceBox.Value = s.LineSpacePx;
            SmartHorizonBox.SelectedIndex = Math.Clamp(s.SmartHorizon, 0, 2);
            LyricsIntervalBox.Value = s.LyricsIntervalPx;
            AllowBitingCheck.IsChecked = s.AllowBiting;
            RubyIntervalBox.Value = s.RubyIntervalPx;
            RubyAlignmentBox.SelectedIndex = Math.Clamp(s.RubyAlignment, 0, 2);
            LyricsAndRubyIntervalBox.Value = s.LyricsAndRubyIntervalPx;
            BuildAlignmentRows(s.HorizontalAlignments);
        }
        finally
        {
            _loading = false;
        }
        UpdateLayoutState();
    }

    /// <summary>行ごとの左右の欄（行ごとに「n 行目 [左寄せ/中央/右寄せ] [×]」）。</summary>
    private void BuildAlignmentRows(IReadOnlyList<int> alignments)
    {
        AlignmentRows.Children.Clear();
        for (int i = 0; i < alignments.Count; i++)
        {
            int row = i;
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = $"{i + 1} 行目", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = AlignmentNames, SelectedIndex = Math.Clamp(alignments[i], 0, 2) };
            box.SelectionChanged += (_, _) =>
            {
                if (_loading || box.SelectedIndex < 0) return;
                Edit(l =>
                {
                    if (row < l.HorizontalAlignments.Count) l.HorizontalAlignments[row] = box.SelectedIndex;
                });
            };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Padding = new Thickness(8, 6, 8, 6),
                IsEnabled = alignments.Count > 1,
            };
            ToolTipService.SetToolTip(remove, "この行を消します");
            remove.Click += (_, _) =>
            {
                Edit(l =>
                {
                    if (l.HorizontalAlignments.Count > 1 && row < l.HorizontalAlignments.Count) l.HorizontalAlignments.RemoveAt(row);
                });
                if (CurrentSettings() is { } s) BuildAlignmentRows(s.HorizontalAlignments);
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
            AlignmentRows.Children.Add(grid);
        }
    }

    private void OnAddAlignmentClick(object sender, RoutedEventArgs e)
    {
        Edit(l => l.HorizontalAlignments.Add(l.HorizontalAlignments.Count > 0 ? l.HorizontalAlignments[^1] : 1));
        if (CurrentSettings() is { } s) BuildAlignmentRows(s.HorizontalAlignments);
    }

    private void OnLayoutFieldChanged(object sender, SelectionChangedEventArgs e) => Edit(ReadFields);

    private void OnLayoutNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Edit(ReadFields);

    private void OnLayoutCheckClick(object sender, RoutedEventArgs e) => Edit(ReadFields);

    /// <summary>値の欄（行ごとの左右以外）を編集するレイアウト設定へ写す（空の欄はそのまま）。</summary>
    private void ReadFields(N3Layout l)
    {
        if (VerticalBox.SelectedIndex >= 0) l.VerticalAlignment = VerticalBox.SelectedIndex;
        if (SmartHorizonBox.SelectedIndex >= 0) l.SmartHorizon = SmartHorizonBox.SelectedIndex;
        if (RubyAlignmentBox.SelectedIndex >= 0) l.RubyAlignment = RubyAlignmentBox.SelectedIndex;
        if (double.IsFinite(VerticalMarginBox.Value)) l.VerticalMarginPx = VerticalMarginBox.Value;
        if (double.IsFinite(HorizontalMarginBox.Value)) l.HorizontalMarginPx = HorizontalMarginBox.Value;
        if (double.IsFinite(LineSpaceBox.Value)) l.LineSpacePx = LineSpaceBox.Value;
        if (double.IsFinite(LyricsIntervalBox.Value)) l.LyricsIntervalPx = LyricsIntervalBox.Value;
        if (double.IsFinite(RubyIntervalBox.Value)) l.RubyIntervalPx = RubyIntervalBox.Value;
        if (double.IsFinite(LyricsAndRubyIntervalBox.Value)) l.LyricsAndRubyIntervalPx = LyricsAndRubyIntervalBox.Value;
        l.AllowBiting = AllowBitingCheck.IsChecked == true;
        VerticalMarginLabel.Text = l.VerticalAlignment switch { 0 => "上余白 px", 2 => "下余白 px", _ => "上下余白 px" };
    }

    /// <summary>選んでいるレイアウト設定を編集する（まだ編集していないベースのものは写してから）。保存して知らせる。</summary>
    private void Edit(Action<N3Layout> change)
    {
        if (_loading || _vm is null || _layoutName is null) return;
        var layout = _vm.EnsureEditableLayout(_layoutName);
        if (layout is null) return;
        change(layout);
        _saving = true;
        try
        {
            _vm.SaveLayouts();
        }
        finally
        {
            _saving = false;
        }
        RefreshLayoutPreview();
    }

    // ------------------------------------------------------------ レイアウト: 名前・追加・元に戻す・適用

    private void OnLayoutNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        CommitLayoutName();
    }

    private void OnLayoutNameLostFocus(object sender, RoutedEventArgs e) => CommitLayoutName();

    private void CommitLayoutName()
    {
        if (_loading || _vm is null || _layoutName is null) return;
        string text = LayoutNameBox.Text.Trim();
        if (text.Length == 0 || text == _layoutName)
        {
            LayoutNameBox.Text = _layoutName;
            return;
        }
        if (_vm.RenameLayout(_layoutName, text) is { } renamed)
        {
            _layoutName = renamed;
            RefreshLayouts();
        }
        else
        {
            LayoutNameBox.Text = _layoutName;
        }
    }

    private void OnLayoutAddClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var source = CurrentSettings();
        var layout = source is null ? new N3Layout() : N3Layout.FromSettings(source);
        layout.Name = "新しいレイアウト";
        _layoutName = _vm.AddLayout(layout).Name;
        RefreshLayouts();
        LayoutNameBox.Focus(FocusState.Programmatic);
        LayoutNameBox.SelectAll();
    }

    private async void OnLayoutRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _layoutName is null || LayoutPicker.SelectedItem is not LayoutPickItem item || !item.Edited) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item.Added ? $"レイアウト設定「{item.Name}」を削除しますか？" : $"レイアウト設定「{item.Name}」を元に戻しますか？",
            Content = item.Added
                ? "この名前を指定している行・タブは、ページの行数から自動で選ぶようになります。"
                : "編集した値を消して、ベースの n3proj（無ければ書き出しの既定）の値に戻します。",
            PrimaryButtonText = item.Added ? "削除" : "元に戻す",
            CloseButtonText = "やめる",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _vm.RemoveEditedLayout(item.Name);
        if (item.Added) _layoutName = null;
        RefreshLayouts();
    }

    private void OnLayoutApplyClick(object sender, RoutedEventArgs e)
    {
        if (_layoutName is not null) LayoutApplyRequested?.Invoke(this, _layoutName);
    }

    // ------------------------------------------------------------ レイアウト: 見本

    /// <summary>見本: 選んだ行（無ければ最初）のページの行を、選んでいるレイアウトで並べる（ワイプ前・全部表示）。</summary>
    private void RefreshLayoutPreview()
    {
        if (_vm is null || LayoutPane.Visibility != Visibility.Visible) return;
        var model = _vm.PreviewModel;
        var layout = CurrentSettings();
        if (model is null || layout is null)
        {
            LayoutPreview.Model = null;
            return;
        }
        var anchor = (_line is not null ? model.Lines.FirstOrDefault(p => ReferenceEquals(p.Source.Line, _line.Model)) : null)
            ?? model.Lines.FirstOrDefault();
        var lines = new List<PreviewLine>();
        if (anchor is not null && model.Pages.TryGetValue((anchor.Tab, anchor.Page), out var page))
        {
            var spacing = new SubtitleSpacing((float)layout.LyricsIntervalPx, (float)layout.RubyIntervalPx, (float)layout.LyricsAndRubyIntervalPx, layout.RubyAlignment, layout.AllowBiting);
            lines = page.Select(p => p with
            {
                Source = p.Source.WithSpacing(spacing),
                Layout = layout,
                BeginMs = 0,
                EndMs = int.MaxValue,
                Wipe = Array.Empty<N3WipeTimeline.Group>(),
            }).ToList();
        }
        LayoutPreview.Model = new SubtitlePreviewModel(model.ScreenWidth, model.ScreenHeight, lines);
        LayoutPreview.TimeMs = 1;
    }

    // ------------------------------------------------------------ 表示時刻（自動調整のパラメーターと実行）

    /// <summary>表示時刻のパラメーターを変えた（設定は保存済み。メイン画面はプレビュー・行設定・チェックを作り直す）。</summary>
    public event EventHandler? ShowTimeSettingsChanged;

    /// <summary>「自動調整を実行」を押した。</summary>
    public event EventHandler? AutoShowTimeRequested;

    /// <summary>表示時刻のタブを出しているか。</summary>
    public bool IsShowTimePaneVisible => ShowTimePane.Visibility == Visibility.Visible;

    /// <summary>表示時刻のタブの欄を設定の値に合わせ、行数の説明を作り直す。</summary>
    public void RefreshShowTimePane()
    {
        if (_vm is null) return;
        var s = _vm.Settings;
        _loading = true;
        try
        {
            StLeadBox.Value = s.DisplayLeadSeconds;
            StTailBox.Value = s.DisplayTailSeconds;
            StIntervalBox.Value = s.N3IntervalSeconds;
            StProtectBox.Value = s.N3ProtectSeconds;
            StOverlapBox.Value = s.N3OverlapSeconds;
            StTopLongCheck.IsChecked = s.N3TopLong;
            StEmojiYieldCheck.IsChecked = s.N3EmojiLeadYield;
            if (_vm.Nkm3Env is { PreTimeMs: not null } env)
            {
                StImportNkm3Button.Visibility = Visibility.Visible;
                StImportNkm3Button.Content = $"ニコカラメーカーの設定値を取り込む（{env.PreTimeMs / 1000.0:0.##} / {env.PostTimeMs / 1000.0:0.##} / {env.IntervalMs / 1000.0:0.##} 秒）";
            }
        }
        finally
        {
            _loading = false;
        }
        RefreshShowTimeSummary();
    }

    /// <summary>表示中のタブの、表示時刻の出どころごとの行数と、実行し直すと変わる行の数を出す。</summary>
    public void RefreshShowTimeSummary()
    {
        if (_vm is null) return;
        var (c, outdated) = _vm.ShowTimeSummary();
        var parts = new List<string>();
        if (c.Manual > 0) parts.Add($"手で直した {c.Manual} 行");
        if (c.Loaded > 0) parts.Add($"読み込んだ {c.Loaded} 行");
        if (c.Auto > 0) parts.Add($"自動調整の {c.Auto} 行");
        if (c.Live > 0) parts.Add($"未設定（自動で計算）{c.Live} 行");
        string text = parts.Count == 0 ? "表示時刻を決められる行がありません" : "表示中のタブ: " + string.Join("・", parts);
        if (outdated > 0) text += $"\n今のパラメーターで実行し直すと {outdated} 行が変わります";
        ShowTimeSummaryText.Text = text;
    }

    private void OnShowTimeNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _vm is null) return;
        if (double.IsNaN(args.NewValue))
        {
            RefreshShowTimePane(); // 空にした欄は元の値に戻す
            return;
        }
        var s = _vm.Settings;
        double v = Math.Max(0, args.NewValue);
        if (ReferenceEquals(sender, StLeadBox)) s.DisplayLeadSeconds = v;
        else if (ReferenceEquals(sender, StTailBox)) s.DisplayTailSeconds = v;
        else if (ReferenceEquals(sender, StIntervalBox)) s.N3IntervalSeconds = v;
        else if (ReferenceEquals(sender, StProtectBox)) s.N3ProtectSeconds = v;
        else if (ReferenceEquals(sender, StOverlapBox)) s.N3OverlapSeconds = v;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnShowTimeCheckClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var s = _vm.Settings;
        if (ReferenceEquals(sender, StTopLongCheck)) s.N3TopLong = StTopLongCheck.IsChecked == true;
        else if (ReferenceEquals(sender, StEmojiYieldCheck)) s.N3EmojiLeadYield = StEmojiYieldCheck.IsChecked == true;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStImportNkm3Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Nkm3Env is not { } env) return;
        var s = _vm.Settings;
        if (env.PreTimeMs is int pre) s.DisplayLeadSeconds = pre / 1000.0;
        if (env.PostTimeMs is int post) s.DisplayTailSeconds = post / 1000.0;
        if (env.IntervalMs is int interval) s.N3IntervalSeconds = interval / 1000.0;
        s.Save();
        RefreshShowTimePane();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRunAutoShowTimeClick(object sender, RoutedEventArgs e) => AutoShowTimeRequested?.Invoke(this, EventArgs.Empty);
}
