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
    private string usageText = "";

    [ObservableProperty]
    private Brush afterBrush = N3BrushPreview.Transparent();

    [ObservableProperty]
    private Brush beforeBrush = N3BrushPreview.Transparent();

    [ObservableProperty]
    private string toolTip = "";

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

    partial void OnUsageChanged(int value) => UsageText = value > 0 ? $"{value} 文字" : "未使用";

    partial void OnIsOverriddenChanged(bool value) => UpdateToolTip();

    private void UpdateToolTip()
    {
        var lines = new List<string> { $"{DisplayName}（{ScopeText}）", FaceText };
        lines.Add(N3BrushPreview.Describe("ワイプ後の文字", Font.Detail.Brushes[0]));
        lines.Add(N3BrushPreview.Describe("ワイプ前の文字", Font.Detail.Brushes[N3FontDetail.BeforeOffset]));
        if (IsLinked) lines.Add("ニコカラメーカー3 のテンプレートと連動中");
        if (IsOverridden) lines.Add("同じ名前の曲専用のフォント設定があるため、この曲の書き出しでは使われません");
        ToolTip = string.Join("\n", lines);
    }
}

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

/// <summary>フォント設定を使っている行 1 つ（使用状況の一覧）。</summary>
/// <param name="Text">表示（タブ名・行番号・行の先頭）。</param>
public sealed record FontUsageLine(string Text);

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
}
