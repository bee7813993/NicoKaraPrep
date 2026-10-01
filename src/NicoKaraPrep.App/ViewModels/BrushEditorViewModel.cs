using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;
using Windows.UI;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 配色 1 箇所（<see cref="N3Brush"/>）の編集欄。塗りの種類・色（ColorPicker・16 進・不透明度）・マーカー・画像。
/// ColorPicker は 1 つだけで、単色なら箇所の色を、グラデーション・ミルフィーユなら選んだマーカーの色を編集する。
/// 配色パターンで同じ役割の箇所（<see cref="LinkedIndices"/>）があれば、編集のたびに編集した箇所の写しをそこへも入れる（まとめて編集）。
/// </summary>
public sealed partial class BrushEditorViewModel : ObservableObject
{
    private readonly FontEditorViewModel _editor;
    private bool _loading;

    public BrushEditorViewModel(FontEditorViewModel editor)
    {
        _editor = editor;
    }

    /// <summary>塗りの種類の選択肢（添字 = <see cref="N3Brush.Type"/>）。</summary>
    public IReadOnlyList<string> TypeChoices { get; } = new[] { "単色", "グラデーション", "ミルフィーユ", "画像" };

    /// <summary>編集している配色の箇所（<see cref="N3FontDetail.Brushes"/> の添字）。</summary>
    public int Index { get; private set; }

    /// <summary>まとめて変える、同じ役割のほかの箇所（無ければ空）。</summary>
    public IReadOnlyList<int> LinkedIndices { get; private set; } = Array.Empty<int>();

    /// <summary>まとめて変える役割の名前（「メイン色」など。まとめないときは空）。</summary>
    private string _roleName = "";

    private N3Brush? Model => _editor.Font?.Detail.Brushes[Index];

    [ObservableProperty]
    private string header = "";

    /// <summary>塗りの種類（0 単色 / 1 グラデーション / 2 ミルフィーユ / 3 画像。範囲外は -1）。</summary>
    [ObservableProperty]
    private int brushType = -1;

    [ObservableProperty]
    private bool isSolid;

    [ObservableProperty]
    private bool isStops;

    [ObservableProperty]
    private bool isBitmap;

    // ------------------------------------------------------------ 色（単色の色、または選んだマーカーの色）

    [ObservableProperty]
    private bool isColorEditorVisible;

    [ObservableProperty]
    private string colorTargetText = "";

    [ObservableProperty]
    private Color pickerColor = Colors.White;

    [ObservableProperty]
    private string hexText = "";

    [ObservableProperty]
    private double alphaValue = 100;

    /// <summary>単色の色が未指定か。</summary>
    [ObservableProperty]
    private bool isColorUnset;

    // ------------------------------------------------------------ マーカー

    public ObservableCollection<GradientStopViewModel> Stops { get; } = new();

    [ObservableProperty]
    private GradientStopViewModel? selectedStop;

    [ObservableProperty]
    private Brush stripBrush = N3BrushPreview.Transparent();

    [ObservableProperty]
    private string stopsNote = "";

    /// <summary>「端を広く配置」で、端の帯を中央の帯より何 % 広くするか（最初は組み合わせのフォント設定の既定。変えた値はビューを開いているあいだ保つ）。</summary>
    [ObservableProperty]
    private double endWidenValue = N3FontComposer.DefaultEndWidenPercent;

    /// <summary><see cref="EndWidenValue"/> を組み合わせのフォント設定の既定で読み込んだか。</summary>
    private bool _endWidenLoaded;

    // ------------------------------------------------------------ 画像

    [ObservableProperty]
    private string bitmapPath = "";

    [ObservableProperty]
    private double bitmapScaleValue = 100;

    [ObservableProperty]
    private string bitmapNote = "";

    // ------------------------------------------------------------ 使っている色（ColorPicker の横）

    /// <summary>役割（メイン色・ベース色など）ごとの使っている色。</summary>
    public ObservableCollection<PaletteGroupItem> PaletteGroups { get; } = new();

    /// <summary>使っている画像。</summary>
    public ObservableCollection<PaletteImageItem> PaletteImages { get; } = new();

    /// <summary>使っている色か画像があるか（無ければ一覧を出さない）。</summary>
    [ObservableProperty]
    private bool hasPalette;

    [ObservableProperty]
    private bool hasPaletteImages;

    /// <summary>最近使った色（ColorPicker・16 進・不透明度で指定した色。新しい順）。</summary>
    public ObservableCollection<PaletteColorItem> RecentColors { get; } = new();

    [ObservableProperty]
    private bool hasRecentColors;

    /// <summary>使っている色・最近使った色のどれかを出すか（どれも無ければ一覧の欄ごと出さない）。</summary>
    [ObservableProperty]
    private bool hasColorLists;

    // ------------------------------------------------------------ 読み込み

    /// <summary>
    /// 配色の箇所を読み込む（編集欄の値をすべて置き換える）。linked は同じ役割でまとめて変えるほかの箇所、roleName はその役割の名前。
    /// </summary>
    public void Load(int index, IReadOnlyList<int>? linked = null, string roleName = "")
    {
        FlushRecent(); // 前の箇所で指定した色を、読み込み直す前に最近使った色へ
        _loading = true;
        try
        {
            if (!_endWidenLoaded)
            {
                EndWidenValue = _editor.ComposeEndWidenPercent;
                _endWidenLoaded = true;
            }
            Index = index;
            LinkedIndices = linked?.Where(i => i != index && i >= 0 && i < N3FontDetail.BrushCount).Distinct().ToList() ?? new List<int>();
            _roleName = LinkedIndices.Count > 0 ? roleName : "";
            Header = LinkedIndices.Count > 0
                ? $"{_roleName}（{N3ColorPatterns.Describe(LinkedIndices.Append(index).OrderBy(i => i))}）をまとめて編集"
                : N3FontDetail.BrushLabels[index];
            if (Model is not { } b) return;
            BrushType = b.Type is >= N3Brush.TypeSolid and <= N3Brush.TypeBitmap ? b.Type : -1;
            UpdateTypeFlags(b);
            LoadStops(keep: null);
            BitmapPath = b.BitmapPath;
            BitmapScaleValue = b.BitmapScale;
            UpdateBitmapNote(b);
            LoadColorTarget();
            UpdateStrip();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateTypeFlags(N3Brush b)
    {
        IsSolid = b.Type == N3Brush.TypeSolid;
        IsStops = b.Type is N3Brush.TypeGradient or N3Brush.TypeMilleFeuille;
        IsBitmap = b.Type == N3Brush.TypeBitmap;
        StopsNote = b.Type == N3Brush.TypeMilleFeuille
            ? "ミルフィーユ: マーカーの色で次のマーカーまでを塗ります（最下段のマーカーの色は使われません）。0% が文字の上端です"
            : "グラデーション: 縦方向（0% が文字の上端、100% が下端）";
    }

    /// <summary>マーカーの一覧を作り直す。keep のマーカーがあれば選び直す。</summary>
    private void LoadStops(N3GradientStop? keep)
    {
        bool was = _loading;
        _loading = true;
        try
        {
            Stops.Clear();
            SelectedStop = null;
            if (Model is not { } b) return;
            for (int i = 0; i < b.Stops.Count; i++)
            {
                var vm = new GradientStopViewModel(this, b.Stops[i], i, canDelete: i > 0 && i < b.Stops.Count - 1);
                Stops.Add(vm);
                if (ReferenceEquals(b.Stops[i], keep)) SelectedStop = vm;
            }
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>ColorPicker・16 進・不透明度の欄を、単色の色か選んだマーカーの色で読み直す。</summary>
    private void LoadColorTarget()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            if (Model is not { } b)
            {
                IsColorEditorVisible = false;
                return;
            }
            if (IsSolid)
            {
                bool valid = N3FontSet.IsValidWeb16(b.Color);
                ColorTargetText = valid ? "単色の色" : "単色の色（未指定: 書き出し時はベースの色、ベースが無ければ既定の色）";
                IsColorUnset = !valid;
                SetColorFields(valid ? b.Color : "FFFFFF", b.AlphaPercent, valid);
                IsColorEditorVisible = true;
            }
            else if (IsStops && SelectedStop is { } s)
            {
                ColorTargetText = $"マーカー {s.Index + 1}（{s.Model.Position * 100:0.#}%）の色";
                IsColorUnset = false;
                SetColorFields(s.Model.Color, s.Model.AlphaPercent, N3FontSet.IsValidWeb16(s.Model.Color));
                IsColorEditorVisible = true;
            }
            else
            {
                ColorTargetText = IsStops ? "マーカーを選ぶと、その色を変えられます" : "";
                IsColorUnset = false;
                IsColorEditorVisible = false;
            }
        }
        finally
        {
            _loading = was;
        }
    }

    private void SetColorFields(string web16, int alphaPercent, bool showHex)
    {
        SetPickerColor(N3BrushPreview.ToColor(web16, alphaPercent, Colors.White));
        HexText = showHex ? N3FontSet.NormalizeWeb16(web16) : "";
        AlphaValue = alphaPercent;
    }

    private void UpdateStrip()
    {
        if (Model is not { } b)
        {
            StripBrush = N3BrushPreview.Transparent();
            return;
        }
        StripBrush = b.Type is N3Brush.TypeGradient or N3Brush.TypeMilleFeuille
            ? N3BrushPreview.CreateGradient(N3BrushPreview.EffectiveStops(b), sharp: b.Type == N3Brush.TypeMilleFeuille)
            : N3BrushPreview.Create(b);
    }

    private void UpdateBitmapNote(N3Brush b)
    {
        BitmapNote = b.BitmapPath.Length == 0
            ? "画像ファイルを指定してください"
            : File.Exists(b.BitmapPath) ? "" : "画像が見つかりません（書き出してもニコカラメーカー3 で表示されません）";
    }

    // ------------------------------------------------------------ 編集

    private string Key(string what) => $"brush{Index}.{what}";

    /// <summary>
    /// この箇所の配色を変え、同じ役割でまとめて変える箇所へ写しを入れて、色見本と帯を更新する。
    /// 配色から自動で当てはめていた配色パターンは、ここで編集したフォント設定に覚えさせる（1 箇所だけ変えて形が崩れても、パターンを見失わないように）。
    /// </summary>
    private void Edit(string? key, Action<N3Brush> change)
    {
        if (_loading || _editor.IsLoading || Model is null) return;
        int index = Index;
        var linked = LinkedIndices;
        string? pin = _editor.PatternIdToKeep;
        _editor.Edit(key, f =>
        {
            change(f.Detail.Brushes[index]);
            foreach (int i in linked) f.Detail.Brushes[i] = f.Detail.Brushes[index].Clone();
            if (pin is not null) f.ColorPatternId ??= pin;
        });
        AfterEdit();
    }

    private void AfterEdit()
    {
        _editor.RefreshBrushCell(Index);
        foreach (int i in LinkedIndices) _editor.RefreshBrushCell(i);
        UpdateStrip();
        _editor.RefreshPatternState();
    }

    partial void OnBrushTypeChanged(int value)
    {
        if (_loading || value is < N3Brush.TypeSolid or > N3Brush.TypeBitmap || Model is not { } current || current.Type == value) return;
        Edit(null, b =>
        {
            b.Type = value;
            // マーカーが無いままグラデーション・ミルフィーユにしたら、ニコカラメーカー3 の既定の 3 点から始める
            if (value is N3Brush.TypeGradient or N3Brush.TypeMilleFeuille && b.Stops.Count == 0) b.Stops = N3Brush.DefaultStops();
        });
        Load(Index, LinkedIndices, _roleName);
    }

    /// <summary>ColorPicker に最後に設定した色（ColorPicker がその色を返してきたときは、操作ではないので編集として扱わない）。</summary>
    private Color? _pickerColorSet;

    private void SetPickerColor(Color color)
    {
        _pickerColorSet = color;
        PickerColor = color;
    }

    partial void OnPickerColorChanged(Color value)
    {
        if (_loading || value == _pickerColorSet) return;
        _pickerColorSet = null;
        string hex = N3BrushPreview.ToWeb16(value);
        int alpha = N3BrushPreview.AlphaPercent(value.A);
        ApplyColor(hex, alpha, ColorSource.Picker);
    }

    partial void OnHexTextChanged(string value)
    {
        if (_loading || !N3FontSet.IsValidWeb16(value)) return;
        ApplyColor(N3FontSet.NormalizeWeb16(value), null, ColorSource.Hex);
    }

    partial void OnAlphaValueChanged(double value)
    {
        if (_loading || double.IsNaN(value)) return;
        ApplyColor(null, (int)Math.Round(Math.Clamp(value, 0, 100)), ColorSource.Alpha);
    }

    /// <summary>
    /// 単色の色か選んだマーカーの色を変える（null の項目は変えない）。変えたあと、ほかの色の欄を合わせる。
    /// 使っている色の一覧から選んだときは、ColorPicker の操作とまとめず、1 回の 元に戻す で戻るようにする。
    /// </summary>
    private void ApplyColor(string? hex, int? alpha, ColorSource source)
    {
        if (Model is not { } b) return;
        bool separate = source == ColorSource.Palette;
        if (IsSolid)
        {
            Edit(separate ? null : Key("color"), x =>
            {
                if (hex is not null) x.Color = hex;
                else if (!N3FontSet.IsValidWeb16(x.Color)) x.Color = "FFFFFF"; // 未指定の色の不透明度だけを変えたら白にする
                if (alpha is int a) x.AlphaPercent = a;
            });
            SyncColorFields(b.Color, b.AlphaPercent, source);
            IsColorUnset = false;
            ColorTargetText = "単色の色";
            if (!separate) NoteRecent(b.Color, b.AlphaPercent);
        }
        else if (IsStops && SelectedStop is { } s)
        {
            var stop = s.Model;
            Edit(separate ? null : Key($"stop{s.Index}.color"), _ =>
            {
                if (hex is not null) stop.Color = hex;
                if (alpha is int a) stop.AlphaPercent = a;
            });
            SyncColorFields(stop.Color, stop.AlphaPercent, source);
            s.Refresh();
            if (!separate) NoteRecent(stop.Color, stop.AlphaPercent);
        }
    }

    /// <summary>色を変えた欄（<see cref="Palette"/> は使っている色の一覧。どの欄にも書き戻す）。</summary>
    private enum ColorSource
    {
        Picker,
        Hex,
        Alpha,
        Palette,
    }

    // ------------------------------------------------------------ 使っている色

    /// <summary>使っている色・画像の一覧を入れ替える（色が無い役割は出さない）。</summary>
    public void SetPalette(IReadOnlyList<N3PaletteGroup> groups, IReadOnlyList<N3PaletteImage> images)
    {
        PaletteGroups.Clear();
        foreach (var g in groups.Where(g => g.Colors.Count > 0)) PaletteGroups.Add(new PaletteGroupItem(g));
        PaletteImages.Clear();
        foreach (var image in images) PaletteImages.Add(new PaletteImageItem(image));
        HasPaletteImages = PaletteImages.Count > 0;
        HasPalette = PaletteGroups.Count > 0 || HasPaletteImages;
        HasColorLists = HasPalette || HasRecentColors;
    }

    /// <summary>最近使った色の一覧を入れ替える。</summary>
    public void SetRecentColors(IReadOnlyList<N3PaletteColor> colors)
    {
        RecentColors.Clear();
        foreach (var c in colors) RecentColors.Add(new PaletteColorItem(c, "最近使った色"));
        HasRecentColors = RecentColors.Count > 0;
        HasColorLists = HasPalette || HasRecentColors;
    }

    // ------------------------------------------------------------ 最近使った色に入れる

    /// <summary>最近使った色に入れる前の、指定した色（ドラッグ中や入力の途中は、手が止まるまで待つ）。</summary>
    private (string Color, int Alpha)? _pendingRecent;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _recentTimer;

    /// <summary>ColorPicker の上でドラッグしている最中か（そのあいだは最近使った色に入れない）。</summary>
    private bool _dragging;

    /// <summary>
    /// ColorPicker・16 進・不透明度・マーカーの色の欄で色を指定した。手が止まってから（ドラッグ中は離してから）最近使った色に入れる。
    /// 使っている色・最近使った色の一覧から選んだ色は入れない（呼ばない）。
    /// </summary>
    private void NoteRecent(string color, int alphaPercent)
    {
        if (!N3FontSet.IsValidWeb16(color)) return;
        _pendingRecent = (N3FontSet.NormalizeWeb16(color), alphaPercent);
        if (_recentTimer is null)
        {
            _recentTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_recentTimer is null) return;
            _recentTimer.Interval = TimeSpan.FromSeconds(1.2);
            _recentTimer.IsRepeating = false;
            _recentTimer.Tick += (_, _) =>
            {
                if (!_dragging) FlushRecent();
            };
        }
        _recentTimer.Stop();
        _recentTimer.Start();
    }

    /// <summary>待っている色を最近使った色に入れる（箇所を読み込み直す前・ドラッグを終えたとき・手が止まったとき）。</summary>
    public void FlushRecent()
    {
        _recentTimer?.Stop();
        if (_pendingRecent is not { } p) return;
        _pendingRecent = null;
        _editor.AddRecentColor(p.Color, p.Alpha);
    }

    /// <summary>ColorPicker の上でのドラッグを始めた・終えた（終えたら、指定した色を最近使った色に入れる）。</summary>
    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        if (!dragging && _pendingRecent is not null) FlushRecent();
    }

    /// <summary>
    /// 使っている色を当てる。単色ならその色に、グラデーション・ミルフィーユで選んでいるマーカーがあればマーカーの色にする。
    /// 画像（とマーカーを選んでいないとき）は、その色の単色にする。同じ役割でまとめて変える箇所にも入る。
    /// </summary>
    public void ApplyPaletteColor(N3PaletteColor color)
    {
        if (Model is null) return;
        if (IsSolid || (IsStops && SelectedStop is not null))
        {
            ApplyColor(color.Color, color.AlphaPercent, ColorSource.Palette);
            return;
        }
        Edit(null, b =>
        {
            b.Type = N3Brush.TypeSolid;
            b.Color = color.Color;
            b.AlphaPercent = color.AlphaPercent;
        });
        Load(Index, LinkedIndices, _roleName);
    }

    /// <summary>使っている画像を当てる（その画像と拡大率の画像の塗りにする。同じ役割でまとめて変える箇所にも入る）。</summary>
    public void ApplyPaletteImage(N3PaletteImage image)
    {
        if (Model is null) return;
        Edit(null, b =>
        {
            b.Type = N3Brush.TypeBitmap;
            b.BitmapPath = image.Path;
            b.BitmapScale = image.Scale;
        });
        Load(Index, LinkedIndices, _roleName);
    }

    /// <summary>色を変えたあと、変えた欄以外（ColorPicker・16 進・不透明度）を合わせる。</summary>
    private void SyncColorFields(string web16, int alphaPercent, ColorSource source)
    {
        bool was = _loading;
        _loading = true;
        try
        {
            // 変えた欄には書き戻さない（ColorPicker は色相などが丸めで動き、16 進の欄は入力中の文字が置き換わるため）
            if (source != ColorSource.Picker) SetPickerColor(N3BrushPreview.ToColor(web16, alphaPercent, Colors.White));
            if (source != ColorSource.Hex) HexText = N3FontSet.NormalizeWeb16(web16);
            if (source != ColorSource.Alpha) AlphaValue = alphaPercent;
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>単色の色を未指定に戻す（書き出し時はベースの色、ベースが無ければ既定の色）。</summary>
    public void ClearColor()
    {
        if (!IsSolid) return;
        Edit(null, b =>
        {
            b.Color = "";
            b.AlphaPercent = 100;
        });
        LoadColorTarget();
    }

    partial void OnSelectedStopChanged(GradientStopViewModel? value)
    {
        if (_loading) return;
        LoadColorTarget();
    }

    /// <summary>マーカーの位置（%）を変える。並びが変わったら位置の順に並べ直す。</summary>
    internal void SetStopPosition(GradientStopViewModel stop, double percent)
    {
        if (_loading || Model is not { } b) return;
        var target = stop.Model;
        double position = Math.Clamp(percent, 0, 100) / 100;
        bool reorder = false;
        Edit(Key($"stop{stop.Index}.position"), x =>
        {
            target.Position = position;
            var sorted = x.Stops.OrderBy(s => s.Position).ToList();
            reorder = !sorted.SequenceEqual(x.Stops);
            if (reorder) x.Stops = sorted;
        });
        if (reorder)
        {
            // 並びが変わったら一覧を作り直す。位置の欄（NumberBox）の確定の処理の途中でその行を消さないよう、処理が終わってから行う
            int index = Index;
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                if (Index != index || Model is not { } current || !current.Stops.Contains(target)) return;
                LoadStops(keep: target);
                LoadColorTarget();
            });
            return;
        }
        stop.Refresh();
        if (ReferenceEquals(SelectedStop?.Model, target)) LoadColorTarget();
    }

    /// <summary>マーカーの色（16 進）・不透明度を一覧の欄から変える。</summary>
    internal void SetStopColor(GradientStopViewModel stop, string? hex, int? alpha)
    {
        if (_loading || Model is null) return;
        var target = stop.Model;
        Edit(Key($"stop{stop.Index}.color"), _ =>
        {
            if (hex is not null) target.Color = hex;
            if (alpha is int a) target.AlphaPercent = a;
        });
        stop.Refresh();
        if (ReferenceEquals(SelectedStop, stop)) LoadColorTarget();
        NoteRecent(target.Color, target.AlphaPercent);
    }

    /// <summary>
    /// マーカーを足す。選んだマーカーと次のマーカーの間（選んでいなければ、いちばん広い間）に、上側のマーカーと同じ色で入れる。
    /// </summary>
    public void AddStop()
    {
        if (Model is not { } b || !IsStops) return;
        N3GradientStop? added = null;
        Edit(null, x =>
        {
            if (x.Stops.Count == 0) x.Stops = N3Brush.DefaultStops();
            var stops = x.Stops;
            int at;
            if (SelectedStop is { } sel && stops.IndexOf(sel.Model) is int si && si >= 0 && si < stops.Count - 1)
            {
                at = si;
            }
            else
            {
                at = 0;
                double widest = -1;
                for (int i = 0; i < stops.Count - 1; i++)
                {
                    double gap = stops[i + 1].Position - stops[i].Position;
                    if (gap > widest)
                    {
                        widest = gap;
                        at = i;
                    }
                }
            }
            var upper = stops[at];
            double next = at + 1 < stops.Count ? stops[at + 1].Position : 1;
            added = new N3GradientStop
            {
                Position = Math.Round((upper.Position + next) / 2, 4),
                Color = upper.Color,
                AlphaPercent = upper.AlphaPercent,
            };
            stops.Insert(Math.Min(at + 1, stops.Count), added);
        });
        LoadStops(keep: added);
        LoadColorTarget();
    }

    /// <summary>マーカーを消す（両端は消せない）。</summary>
    public void DeleteStop(GradientStopViewModel stop)
    {
        if (!stop.CanDelete || Model is null) return;
        var target = stop.Model;
        Edit(null, x => x.Stops.Remove(target));
        LoadStops(keep: null);
        LoadColorTarget();
    }

    /// <summary>
    /// マーカーを、端の帯ほど広くなる位置に並べ直す（色の並びはそのまま。端の帯を中央の帯より <see cref="EndWidenValue"/> % 広く。
    /// ミルフィーユは最後のマーカーを 100% に置く。組み合わせのフォント設定と同じ並べ方）。
    /// </summary>
    public void WidenEndStops()
    {
        if (Model is not { } b || b.Stops.Count < 2) return;
        var keep = SelectedStop?.Model;
        double widen = double.IsNaN(EndWidenValue) ? N3FontComposer.DefaultEndWidenPercent : Math.Clamp(EndWidenValue, 0, 200);
        Edit(null, x => N3FontComposer.WidenEnds(x, widen));
        LoadStops(keep);
        LoadColorTarget();
    }

    /// <summary>マーカーを均等な位置に並べる（0%, 1/(n-1), …, 100%）。</summary>
    public void DistributeStops()
    {
        if (Model is not { } b || b.Stops.Count < 2) return;
        var keep = SelectedStop?.Model;
        Edit(null, x =>
        {
            int n = x.Stops.Count;
            for (int i = 0; i < n; i++) x.Stops[i].Position = Math.Round((double)i / (n - 1), 4);
        });
        LoadStops(keep);
        LoadColorTarget();
    }

    partial void OnBitmapPathChanged(string value)
    {
        if (_loading || Model is not { } b) return;
        string path = value.Trim();
        if (path == b.BitmapPath) return;
        Edit(Key("bitmap"), x => x.BitmapPath = path);
        UpdateBitmapNote(b);
    }

    partial void OnBitmapScaleValueChanged(double value)
    {
        if (_loading || double.IsNaN(value) || Model is null) return;
        int scale = (int)Math.Round(Math.Clamp(value, 1, 1000));
        Edit(Key("bitmapScale"), x => x.BitmapScale = scale);
    }
}

/// <summary>グラデーション・ミルフィーユのマーカー 1 つ（マーカーの一覧の 1 行）。</summary>
public sealed partial class GradientStopViewModel : ObservableObject
{
    private readonly BrushEditorViewModel _owner;
    private bool _loading;

    public GradientStopViewModel(BrushEditorViewModel owner, N3GradientStop model, int index, bool canDelete)
    {
        _owner = owner;
        Model = model;
        Index = index;
        CanDelete = canDelete;
        Refresh();
    }

    /// <summary>フォント設定のマーカーそのもの。</summary>
    public N3GradientStop Model { get; }

    /// <summary>一覧の中の位置（0 始まり）。</summary>
    public int Index { get; }

    /// <summary>消せるか（両端のマーカーは消せない）。</summary>
    public bool CanDelete { get; }

    public string DeleteToolTip => CanDelete ? "このマーカーを削除" : "両端のマーカーは削除できません";

    public override string ToString() => $"マーカー {Index + 1}: {Model.Position * 100:0.#}% #{N3FontSet.NormalizeWeb16(Model.Color)} {Model.AlphaPercent}%";

    [ObservableProperty]
    private double positionValue;

    [ObservableProperty]
    private string hexText = "";

    [ObservableProperty]
    private double alphaValue = 100;

    [ObservableProperty]
    private Brush swatch = N3BrushPreview.Transparent();

    /// <summary>マーカーの値から表示を読み直す。</summary>
    public void Refresh()
    {
        _loading = true;
        try
        {
            PositionValue = Math.Round(Model.Position * 100, 2);
            // 入力中の欄と同じ色なら書き換えない（小文字で入力したときなどに、入力位置が先頭へ戻らないように）
            string hex = N3FontSet.NormalizeWeb16(Model.Color);
            if (!string.Equals(N3FontSet.NormalizeWeb16(HexText), hex, StringComparison.Ordinal)) HexText = hex;
            AlphaValue = Model.AlphaPercent;
            Swatch = new SolidColorBrush(N3BrushPreview.ToColor(Model.Color, Model.AlphaPercent, N3BrushPreview.UnsetColor));
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnPositionValueChanged(double value)
    {
        if (_loading || double.IsNaN(value)) return;
        _owner.SetStopPosition(this, value);
    }

    partial void OnHexTextChanged(string value)
    {
        if (_loading || !N3FontSet.IsValidWeb16(value)) return;
        _owner.SetStopColor(this, N3FontSet.NormalizeWeb16(value), null);
    }

    partial void OnAlphaValueChanged(double value)
    {
        if (_loading || double.IsNaN(value)) return;
        _owner.SetStopColor(this, null, (int)Math.Round(Math.Clamp(value, 0, 100)));
    }
}
