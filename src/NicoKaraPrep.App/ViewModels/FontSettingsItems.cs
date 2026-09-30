using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>フォント設定ビューの一覧の 1 行（アプリ共通または曲専用のフォント設定 1 件）。</summary>
public sealed partial class FontListItem : ObservableObject
{
    public FontListItem(N3FontSet font, bool isSong)
    {
        Font = font;
        IsSong = isSong;
        Refresh();
    }

    /// <summary>フォント設定（アプリ共通の一覧か曲専用の一覧の要素そのもの）。</summary>
    public N3FontSet Font { get; }

    /// <summary>曲専用のフォント設定か（false はアプリ共通）。</summary>
    public bool IsSong { get; }

    public string Id => Font.Id;

    public string ScopeText => IsSong ? "この曲専用" : "アプリ共通";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string faceText = "";

    [ObservableProperty]
    private bool isLinked;

    /// <summary>同じ名前の曲専用のフォント設定があり、書き出しではそちらが使われる（アプリ共通の行だけ）。</summary>
    [ObservableProperty]
    private bool isOverridden;

    /// <summary>このフォント設定が適用される文字数（全タブ）。</summary>
    [ObservableProperty]
    private int usage;

    [ObservableProperty]
    private string usageText = "未使用";

    [ObservableProperty]
    private Brush afterBrush = N3BrushPreview.Transparent();

    [ObservableProperty]
    private Brush beforeBrush = N3BrushPreview.Transparent();

    [ObservableProperty]
    private string toolTip = "";

    /// <summary>階層の中の場所（上の階層の名前を「›」でつないだもの。最上位・曲専用は空）。検索の結果の一覧に出す。</summary>
    [ObservableProperty]
    private string pathText = "";

    /// <summary>階層の中の場所を出すか（<see cref="PathText"/> が空でない）。</summary>
    public bool HasPath => PathText.Length > 0;

    partial void OnPathTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPath));
        UpdateToolTip();
    }

    /// <summary>フォント設定の内容から表示を作り直す（名前・書体・連動・色見本）。</summary>
    public void Refresh()
    {
        DisplayName = Font.Name.Length > 0 ? Font.Name : "（名前なし）";
        FaceText = N3BrushPreview.DescribeFace(Font);
        IsLinked = Font.NkmSynchronize;
        AfterBrush = N3BrushPreview.Create(Font.Detail.Brushes[0]);
        BeforeBrush = N3BrushPreview.Create(Font.Detail.Brushes[N3FontDetail.BeforeOffset]);
        UpdateToolTip();
    }

    /// <summary>読み上げ・UI オートメーションでの名前（一覧の行の名前になる）。</summary>
    public override string ToString() => $"{DisplayName}（{ScopeText}{(IsLinked ? "・連動" : "")}）";

    /// <summary>開いている歌詞のどこかで使われているか（階層では使われているものだけ文字数を出す）。</summary>
    public bool HasUsage => Usage > 0;

    partial void OnUsageChanged(int value)
    {
        UsageText = value > 0 ? $"{value} 文字" : "未使用";
        OnPropertyChanged(nameof(HasUsage));
    }

    partial void OnIsOverriddenChanged(bool value) => UpdateToolTip();

    private void UpdateToolTip()
    {
        var lines = new List<string> { $"{DisplayName}（{ScopeText}）", FaceText };
        if (PathText.Length > 0) lines.Insert(1, $"場所: {PathText}");
        lines.Add(N3BrushPreview.Describe("ワイプ後の文字", Font.Detail.Brushes[0]));
        lines.Add(N3BrushPreview.Describe("ワイプ前の文字", Font.Detail.Brushes[N3FontDetail.BeforeOffset]));
        if (IsLinked) lines.Add("ニコカラメーカー3 のテンプレートと連動中");
        if (IsOverridden) lines.Add("同じ名前の曲専用のフォント設定があるため、この曲の書き出しでは使われません");
        ToolTip = string.Join("\n", lines);
    }
}

/// <summary>左の一覧の階層の節の種類。</summary>
public enum FontTreeKind
{
    /// <summary>フォント設定 1 件（アプリ共通・この曲専用）。</summary>
    Font,

    /// <summary>フォルダ（アプリ共通の階層の、フォント設定ではないまとまり）。</summary>
    Folder,

    /// <summary>「この曲専用」のまとまり（曲専用のフォント設定があるときだけ最上位の先頭に出す）。</summary>
    SongGroup,
}

/// <summary>
/// 左の一覧の階層（ツリー）の節 1 つ。フォント設定・フォルダ・「この曲専用」のまとまりのどれか。
/// フォント設定の節は <see cref="Font"/>（検索の結果の一覧と同じ行）を持ち、表示はそれに従う。
/// </summary>
public sealed partial class FontTreeItem : ObservableObject
{
    /// <summary>「この曲専用」のまとまりの識別子。</summary>
    public const string SongGroupKey = "song-group";

    private FontTreeItem(FontTreeKind kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    /// <summary>フォント設定の節（<paramref name="node"/> はアプリ共通の階層の節。曲専用は null）。</summary>
    public static FontTreeItem ForFont(FontListItem font, N3FontTreeNode? node) => new(FontTreeKind.Font, font.Id) { Font = font, Node = node };

    /// <summary>フォルダの節。</summary>
    public static FontTreeItem ForFolder(N3FontTreeNode node) => new(FontTreeKind.Folder, node.Key) { Node = node, Name = node.FolderName ?? "" };

    /// <summary>「この曲専用」のまとまり。</summary>
    public static FontTreeItem ForSongGroup() => new(FontTreeKind.SongGroup, SongGroupKey) { Name = "この曲専用" };

    public FontTreeKind Kind { get; }

    /// <summary>節の識別子（フォント設定は Id、フォルダはフォルダの識別子、まとまりは <see cref="SongGroupKey"/>）。</summary>
    public string Key { get; }

    /// <summary>フォント設定の節の行（フォルダ・まとまりは null）。</summary>
    public FontListItem? Font { get; private init; }

    /// <summary>アプリ共通の階層の節（曲専用のフォント設定・まとまりは null）。</summary>
    public N3FontTreeNode? Node { get; private init; }

    /// <summary>親の節（最上位は null）。</summary>
    public FontTreeItem? Parent { get; set; }

    /// <summary>下の節。</summary>
    public List<FontTreeItem> Children { get; } = new();

    public bool IsFont => Kind == FontTreeKind.Font;

    /// <summary>フォルダかまとまり（フォント設定ではない節）か。</summary>
    public bool IsGroup => Kind != FontTreeKind.Font;

    public bool IsFolder => Kind == FontTreeKind.Folder;

    public bool IsSongGroup => Kind == FontTreeKind.SongGroup;

    /// <summary>フォルダ・まとまりの記号（Segoe Fluent Icons。まとまりは音符、フォルダはフォルダ）。</summary>
    public string Glyph => IsSongGroup ? "" : "";

    /// <summary>フォルダ・まとまりの名前。</summary>
    [ObservableProperty]
    private string name = "";

    /// <summary>フォルダ・まとまりの中のフォント設定の数（下の階層も含む）。</summary>
    [ObservableProperty]
    private string countText = "";

    /// <summary>フォルダ・まとまりの説明（ツールチップ）。</summary>
    [ObservableProperty]
    private string groupToolTip = "";

    /// <summary>節の名前（フォント設定は名前、フォルダ・まとまりは名前）。</summary>
    public string Label => Font?.Font.Name ?? Name;

    /// <summary>読み上げ・UI オートメーションでの名前（ツリーの節の名前になる）。</summary>
    public override string ToString() => Kind switch
    {
        FontTreeKind.Font => Font!.ToString(),
        FontTreeKind.Folder => $"{Name}（フォルダ）",
        _ => Name,
    };
}

/// <summary>「移動...」の移し先 1 つ（フォルダかフォント設定。Key が null なら最上位）。</summary>
/// <param name="Key">移し先の節の識別子（最上位は null）。</param>
/// <param name="Label">表示（名前）。</param>
/// <param name="Depth">階層の深さ（最上位の節が 0。「いちばん上の階層」も 0）。</param>
/// <param name="IsFolder">フォルダか（最上位も true）。</param>
/// <param name="Path">上の階層の名前（検索の結果に出す）。</param>
public sealed record FontMoveTarget(string? Key, string Label, int Depth, bool IsFolder, string Path)
{
    /// <summary>字下げの幅（px）。</summary>
    public Microsoft.UI.Xaml.Thickness Indent => new(Depth * 18, 0, 0, 0);

    /// <summary>記号（フォルダ・フォント設定）。</summary>
    public string Glyph => Key is null ? "" : IsFolder ? "" : "";

    /// <summary>上の階層の名前を出すか（検索の結果として平らに並べるとき）。</summary>
    public bool ShowPath { get; init; }

    public override string ToString() => Key is null ? Label : Path.Length > 0 ? $"{Label}（{Path}）" : Label;
}

/// <summary>ツリーでドラッグしたあとの節の並び（ビューから ViewModel へ渡す）。</summary>
/// <param name="Key">節の識別子（<see cref="FontTreeItem.Key"/>）。</param>
/// <param name="Children">下の節。</param>
public sealed record DroppedTreeNode(string Key, IReadOnlyList<DroppedTreeNode> Children);

/// <summary>配色の 8 箇所のボタン 1 つ（色見本）。</summary>
public sealed partial class BrushCellViewModel : ObservableObject
{
    private static readonly string[] PartLabels = { "文字", "縁", "縁 2", "飾り" };

    public BrushCellViewModel(int index)
    {
        Index = index;
        Label = PartLabels[index % N3FontDetail.BeforeOffset];
    }

    /// <summary>配色の箇所（<see cref="N3FontDetail.Brushes"/> の添字）。</summary>
    public int Index { get; }

    /// <summary>箇所の短い名前（文字・縁・縁 2・飾り）。</summary>
    public string Label { get; }

    [ObservableProperty]
    private Brush swatch = N3BrushPreview.Transparent();

    /// <summary>塗りの種類の短い表示（単色は空）。</summary>
    [ObservableProperty]
    private string typeText = "";

    /// <summary>単色で色が未指定か（見本に「未指定」と出す）。</summary>
    [ObservableProperty]
    private bool isBlank;

    [ObservableProperty]
    private string toolTip = "";

    /// <summary>下の編集欄で編集している箇所か。</summary>
    [ObservableProperty]
    private bool isSelected;

    /// <summary>配色パターンでのこの箇所の役割の名前（「キャラ色」など。個別・パターンなしは空）。</summary>
    [ObservableProperty]
    private string roleText = "";

    /// <summary>役割があるか（<see cref="RoleText"/> が空でない）。</summary>
    public bool HasRole => RoleText.Length > 0;

    partial void OnRoleTextChanged(string value) => OnPropertyChanged(nameof(HasRole));

    /// <summary>編集している箇所と同じ役割で、まとめて変わる箇所か（枠を薄く強調する）。</summary>
    [ObservableProperty]
    private bool isLinked;

    /// <summary>配色パターンの役割と色がそろっていない箇所か（印を出す）。</summary>
    [ObservableProperty]
    private bool isDeviation;

    public void Load(N3Brush brush)
    {
        Swatch = N3BrushPreview.Create(brush);
        IsBlank = N3BrushPreview.IsBlank(brush);
        TypeText = brush.Type switch
        {
            N3Brush.TypeGradient => "グラデ",
            N3Brush.TypeMilleFeuille => "ミルフィーユ",
            N3Brush.TypeBitmap => "画像",
            _ => "",
        };
        ToolTip = N3BrushPreview.Describe(N3FontDetail.BrushLabels[Index], brush);
    }
}

/// <summary>見づらい配色を直す色の候補 1 つ（配色の欄のボタン）。見本は候補の色で文字と縁を並べたもの。</summary>
public sealed class ContrastSuggestionItem
{
    public ContrastSuggestionItem(N3ContrastSuggestion suggestion, N3ContrastIssue issue, string label)
    {
        Suggestion = suggestion;
        Label = label;
        bool text = suggestion.Slot == issue.TextSlot;
        SampleText = new SolidColorBrush(N3BrushPreview.ToColor(text ? suggestion.Color : issue.TextColor, 100, Microsoft.UI.Colors.White));
        SampleEdge = new SolidColorBrush(N3BrushPreview.ToColor(text ? issue.EdgeColor : suggestion.Color, 100, Microsoft.UI.Colors.White));
        ToolTip = $"{label}（コントラスト比 {issue.Ratio:0.0} → {suggestion.Ratio:0.0}）";
    }

    public N3ContrastSuggestion Suggestion { get; }

    /// <summary>候補の説明（「キャラ色を暗くして #B3B300 に」など）。</summary>
    public string Label { get; }

    /// <summary>見本の文字の色。</summary>
    public Brush SampleText { get; }

    /// <summary>見本の縁（背景）の色。</summary>
    public Brush SampleEdge { get; }

    public string ToolTip { get; }

    public override string ToString() => Label;
}

/// <summary>フォント設定を使っている行 1 つ（使用状況の一覧）。</summary>
/// <param name="Text">表示（タブ名・行番号・行の先頭）。</param>
public sealed record FontUsageLine(string Text)
{
    public override string ToString() => Text;
}

/// <summary>フォント設定の検証結果 1 件（右ペインの一覧）。</summary>
public sealed class FontIssueItem
{
    public FontIssueItem(N3FontIssue issue)
    {
        Issue = issue;
        Glyph = issue.Severity switch
        {
            IssueSeverity.Error => "",
            IssueSeverity.Warning => "",
            _ => "",
        };
        GlyphBrush = new SolidColorBrush(issue.Severity switch
        {
            IssueSeverity.Error => Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C),
            IssueSeverity.Warning => Windows.UI.Color.FromArgb(255, 0xC1, 0x7A, 0x00),
            _ => Windows.UI.Color.FromArgb(255, 0x00, 0x78, 0xD4),
        });
    }

    public N3FontIssue Issue { get; }

    public string Message => Issue.Message;

    /// <summary>Segoe Fluent Icons の記号（エラー・警告・情報）。</summary>
    public string Glyph { get; }

    public Brush GlyphBrush { get; }

    public string SeverityText => Issue.Severity switch
    {
        IssueSeverity.Error => "エラー",
        IssueSeverity.Warning => "警告",
        _ => "情報",
    };

    public override string ToString() => $"{SeverityText}: {Message}";
}
