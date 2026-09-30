using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>配色パターンの編集画面の一覧の 1 行。</summary>
public sealed partial class ColorPatternRow : ObservableObject
{
    public ColorPatternRow(N3ColorPattern pattern)
    {
        Pattern = pattern;
        Refresh();
    }

    /// <summary>パターン（自作のものは編集中の写し）。</summary>
    public N3ColorPattern Pattern { get; }

    [ObservableProperty]
    private string display = "";

    /// <summary>名前の表示を読み直す。</summary>
    public void Refresh() => Display = ColorPatternDialog.DisplayName(Pattern);

    /// <summary>読み上げ・UI オートメーションでの名前。</summary>
    public override string ToString() => Display;
}

/// <summary>
/// 配色パターンの編集画面。標準のパターン（変えられない）と自作のパターンを並べ、自作のものは名前・役割の名前（最大 4 つ）・
/// 8 箇所の役割を変えられる。新しいフォント設定に使うパターンもここで選ぶ。
/// 「保存」を押したら <see cref="UserPatterns"/>・<see cref="DefaultPatternId"/> に結果が入る（呼び出し側で保存する）。
/// </summary>
public sealed partial class ColorPatternDialog : ContentDialog
{
    private static readonly string[] Letters = { "A", "B", "C", "D" };
    private static readonly string[] PartLabels = { "文字", "縁", "縁 2", "飾り" };

    private readonly TextBox[] _roleBoxes;
    private readonly ComboBox[] _slotBoxes = new ComboBox[N3FontDetail.BrushCount];
    private ColorPatternRow? _current;
    private bool _loading;

    /// <summary>新しいフォント設定に使うパターンの識別子（選択欄で選び直すたびに更新する）。</summary>
    private string _defaultId;

    /// <param name="builtIns">標準のパターン。</param>
    /// <param name="user">自作のパターン（写しを渡す。この画面の中で直接書き換える）。</param>
    /// <param name="defaultId">新しいフォント設定に使うパターンの識別子。</param>
    /// <param name="selectId">最初に選んでおくパターン（無ければ先頭）。</param>
    public ColorPatternDialog(IReadOnlyList<N3ColorPattern> builtIns, List<N3ColorPattern> user, string defaultId, string? selectId)
    {
        foreach (var p in builtIns) Rows.Add(new ColorPatternRow(p));
        foreach (var p in user)
        {
            // 編集しやすいよう、役割の名前は常に 4 つ並べる（保存するときに空の役割を詰める）
            while (p.Roles.Count < N3ColorPattern.MaxRoles) p.Roles.Add("");
            Rows.Add(new ColorPatternRow(p));
        }
        DefaultPatternId = defaultId;
        _defaultId = defaultId;

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 960d;
        _roleBoxes = new[] { RoleABox, RoleBBox, RoleCBox, RoleDBox };
        BuildSlotGrid();
        RebuildDefaultChoices();
        DefaultPatternBox.SelectionChanged += (_, _) =>
        {
            if (_loading || DefaultPatternBox.SelectedIndex < 0) return;
            _defaultId = DefaultPatternBox.SelectedIndex > 0 && DefaultPatternBox.SelectedIndex - 1 < Rows.Count
                ? Rows[DefaultPatternBox.SelectedIndex - 1].Pattern.Id
                : N3ColorPatterns.NoneId;
        };

        var first = Rows.FirstOrDefault(r => r.Pattern.Id == selectId) ?? Rows.FirstOrDefault();
        PatternList.SelectedItem = first;
        if (first is not null) LoadPattern(first);
        PrimaryButtonClick += (_, _) => Commit();
    }

    /// <summary>一覧の行（標準 → 自作）。</summary>
    public ObservableCollection<ColorPatternRow> Rows { get; } = new();

    /// <summary>保存する自作のパターン（「保存」のあと）。</summary>
    public List<N3ColorPattern> UserPatterns { get; private set; } = new();

    /// <summary>新しいフォント設定に使うパターンの識別子（「保存」のあと。パターンを使わないときは <see cref="N3ColorPatterns.NoneId"/>）。</summary>
    public string DefaultPatternId { get; private set; }

    /// <summary>一覧・選択肢に出す名前（標準のものは「（標準）」を付ける）。</summary>
    internal static string DisplayName(N3ColorPattern p)
    {
        string name = p.Name.Trim().Length > 0 ? p.Name.Trim() : "（名前なし）";
        return p.IsBuiltIn ? $"{name}（標準）" : name;
    }

    // ------------------------------------------------------------ 画面を作る

    /// <summary>8 箇所の役割の選択欄（見出し「文字・縁・縁 2・飾り」、行「ワイプ後・ワイプ前」）を作る。</summary>
    private void BuildSlotGrid()
    {
        for (int c = 0; c < PartLabels.Length; c++)
        {
            var header = new TextBlock { Text = PartLabels[c], FontSize = 12 };
            Grid.SetColumn(header, c + 1);
            SlotGrid.Children.Add(header);
        }
        for (int r = 0; r < 2; r++)
        {
            var label = new TextBlock { Text = r == 0 ? "ワイプ後" : "ワイプ前", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, r + 1);
            SlotGrid.Children.Add(label);
            for (int c = 0; c < PartLabels.Length; c++)
            {
                int slot = r * N3FontDetail.BeforeOffset + c;
                var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = slot };
                AutomationProperties.SetName(box, $"{N3FontDetail.BrushLabels[slot]} の役割");
                box.SelectionChanged += OnSlotRoleChanged;
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, c + 1);
                SlotGrid.Children.Add(box);
                _slotBoxes[slot] = box;
            }
        }
    }

    /// <summary>新しいフォント設定に使うパターンの選択肢を作り直す（名前を変えた・追加・削除したとき）。</summary>
    private void RebuildDefaultChoices()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            // 既定にしていたパターンを消したときは、標準の「キャラ色の反転」に戻す
            if (_defaultId != N3ColorPatterns.NoneId && Rows.All(r => r.Pattern.Id != _defaultId)) _defaultId = N3ColorPatterns.CharaInverseId;
            DefaultPatternBox.Items.Clear();
            DefaultPatternBox.Items.Add("パターンを使わない（ニコカラメーカー3 の既定の色）");
            foreach (var row in Rows) DefaultPatternBox.Items.Add(row.Display);
            int index = Rows.ToList().FindIndex(r => r.Pattern.Id == _defaultId);
            DefaultPatternBox.SelectedIndex = index >= 0 ? index + 1 : 0;
        }
        finally
        {
            _loading = was;
        }
    }

    // ------------------------------------------------------------ 読み込み

    private void OnPatternSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatternList.SelectedItem is ColorPatternRow row) LoadPattern(row);
    }

    private void LoadPattern(ColorPatternRow row)
    {
        _loading = true;
        try
        {
            _current = row;
            var p = row.Pattern;
            bool editable = !p.IsBuiltIn;
            ReadOnlyNote.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
            PatternNameBox.Text = p.Name;
            PatternNameBox.IsEnabled = editable;
            for (int i = 0; i < _roleBoxes.Length; i++)
            {
                _roleBoxes[i].Text = i < p.Roles.Count ? p.Roles[i] : "";
                _roleBoxes[i].IsEnabled = editable;
            }
            RefreshSlotChoices();
            foreach (var box in _slotBoxes) box.IsEnabled = editable;
            DeletePatternButton.IsEnabled = editable;
            UpdateSummary();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>8 箇所の選択欄の選択肢（個別・役割 A〜D）を、今の役割の名前で作り直し、今の割り当てを選ぶ。</summary>
    private void RefreshSlotChoices()
    {
        if (_current is not { } row) return;
        var p = row.Pattern;
        bool was = _loading;
        _loading = true;
        try
        {
            int roleCount = p.IsBuiltIn ? p.Roles.Count : N3ColorPattern.MaxRoles;
            for (int slot = 0; slot < _slotBoxes.Length; slot++)
            {
                var box = _slotBoxes[slot];
                box.Items.Clear();
                box.Items.Add("個別");
                for (int r = 0; r < roleCount; r++)
                {
                    string name = r < p.Roles.Count ? p.Roles[r].Trim() : "";
                    box.Items.Add(name.Length > 0 ? $"{Letters[r]}: {name}" : $"{Letters[r]}（使わない）");
                }
                int role = p.Slots is { } s && slot < s.Length ? s[slot] : N3ColorPattern.Individual;
                box.SelectedIndex = role >= 0 && role < roleCount ? role + 1 : 0;
            }
        }
        finally
        {
            _loading = was;
        }
    }

    private void UpdateSummary()
    {
        if (_current is not { } row) return;
        var p = row.Pattern;
        var parts = Enumerable.Range(0, p.Roles.Count)
            .Where(r => p.Roles[r].Trim().Length > 0 && p.SlotsOf(r).Count > 0)
            .Select(r => $"{p.Roles[r].Trim()}: {N3ColorPatterns.Describe(p.SlotsOf(r))}")
            .ToList();
        var individual = Enumerable.Range(0, N3FontDetail.BrushCount)
            .Where(i => p.RoleOf(i) < 0 || p.Roles[p.RoleOf(i)].Trim().Length == 0)
            .ToList();
        if (individual.Count > 0) parts.Add($"個別: {N3ColorPatterns.Describe(individual)}");
        SummaryText.Text = parts.Count > 0 ? string.Join("\n", parts) : "役割がありません（すべての箇所を個別に指定します）";
    }

    // ------------------------------------------------------------ 編集

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is not { Pattern.IsBuiltIn: false } row) return;
        row.Pattern.Name = PatternNameBox.Text;
        row.Refresh();
        RebuildDefaultChoices();
    }

    private void OnRoleNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is not { Pattern.IsBuiltIn: false } row) return;
        int i = Array.IndexOf(_roleBoxes, sender as TextBox);
        if (i < 0) return;
        while (row.Pattern.Roles.Count < N3ColorPattern.MaxRoles) row.Pattern.Roles.Add("");
        row.Pattern.Roles[i] = _roleBoxes[i].Text;
        RefreshSlotChoices();
        UpdateSummary();
    }

    private void OnSlotRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current is not { Pattern.IsBuiltIn: false } row || sender is not ComboBox { Tag: int slot } box || box.SelectedIndex < 0) return;
        row.Pattern.Slots[slot] = box.SelectedIndex - 1;
        UpdateSummary();
    }

    /// <summary>新しいパターンを追加する（役割 A・B だけ名前を付けた空のパターン）。</summary>
    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var p = new N3ColorPattern
        {
            Name = UniqueName("新しいパターン"),
            Roles = new List<string> { "色 A", "色 B", "", "" },
        };
        AddRow(p);
    }

    /// <summary>選んでいるパターンの写しを作る（標準のパターンを元に自分のパターンを作るとき）。</summary>
    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (_current is not { } row) return;
        var p = row.Pattern.Clone();
        p.Id = Guid.NewGuid().ToString();
        p.Name = UniqueName($"{row.Pattern.Name.Trim()} のコピー");
        while (p.Roles.Count < N3ColorPattern.MaxRoles) p.Roles.Add("");
        AddRow(p);
    }

    private void AddRow(N3ColorPattern p)
    {
        var row = new ColorPatternRow(p);
        Rows.Add(row);
        RebuildDefaultChoices();
        PatternList.SelectedItem = row;
        PatternList.ScrollIntoView(row);
        LoadPattern(row);
        DispatcherQueue.TryEnqueue(() =>
        {
            PatternNameBox.Focus(FocusState.Programmatic);
            PatternNameBox.SelectAll();
        });
    }

    /// <summary>選んでいる自作のパターンを消す（標準のパターンは消せない）。</summary>
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_current is not { Pattern.IsBuiltIn: false } row) return;
        int index = Rows.IndexOf(row);
        Rows.Remove(row);
        RebuildDefaultChoices();
        var next = Rows.ElementAtOrDefault(Math.Min(index, Rows.Count - 1));
        PatternList.SelectedItem = next;
        if (next is not null) LoadPattern(next);
    }

    private string UniqueName(string name)
    {
        var names = new HashSet<string>(Rows.Select(r => r.Pattern.Name.Trim()));
        if (!names.Contains(name)) return name;
        int n = 2;
        while (names.Contains($"{name} {n}")) n++;
        return $"{name} {n}";
    }

    // ------------------------------------------------------------ 保存

    /// <summary>
    /// 自作のパターンを整えて結果に入れる: 名前の前後の空白を除き（空なら「名前なしのパターン」）、名前の空の役割を詰めて
    /// 箇所の割り当てを付け替える（空の役割に割り当てていた箇所は個別にする）。
    /// </summary>
    private void Commit()
    {
        UserPatterns = Rows.Where(r => !r.Pattern.IsBuiltIn).Select(r => Normalize(r.Pattern)).ToList();
        DefaultPatternId = _defaultId;
    }

    private static N3ColorPattern Normalize(N3ColorPattern p)
    {
        var map = new int[N3ColorPattern.MaxRoles];
        var roles = new List<string>();
        for (int i = 0; i < N3ColorPattern.MaxRoles; i++)
        {
            string name = i < p.Roles.Count ? p.Roles[i].Trim() : "";
            if (name.Length == 0)
            {
                map[i] = N3ColorPattern.Individual;
                continue;
            }
            map[i] = roles.Count;
            roles.Add(name);
        }
        var slots = new int[N3FontDetail.BrushCount];
        for (int s = 0; s < slots.Length; s++)
        {
            int role = p.Slots is { } src && s < src.Length ? src[s] : N3ColorPattern.Individual;
            slots[s] = role >= 0 && role < map.Length ? map[role] : N3ColorPattern.Individual;
        }
        return new N3ColorPattern
        {
            Id = p.Id,
            Name = p.Name.Trim().Length > 0 ? p.Name.Trim() : "名前なしのパターン",
            Roles = roles,
            Slots = slots,
        };
    }
}
