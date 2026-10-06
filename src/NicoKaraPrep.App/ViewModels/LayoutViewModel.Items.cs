using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>レイアウト設定ビューの表示の文字（上下配置・左右配置の名前など）。</summary>
public static class LayoutTexts
{
    /// <summary>上下配置の名前（0 上寄せ / 1 中央 / 2 下寄せ）。</summary>
    public static readonly string[] VerticalNames = { "上寄せ", "中央", "下寄せ" };

    /// <summary>行ごとの左右配置の名前（0 左寄せ / 1 中央 / 2 右寄せ）。</summary>
    public static readonly string[] HorizontalNames = { "左寄せ", "中央", "右寄せ" };

    /// <summary>スマート水平配置の名前。</summary>
    public static readonly string[] SmartHorizonNames = { "調整しない", "中心位置揃え", "左右余白揃え" };

    /// <summary>ルビ配置の名前。</summary>
    public static readonly string[] RubyAlignmentNames = { "自動配置", "センタリング", "均等割り付け" };

    public static string Vertical(int value) => VerticalNames[Math.Clamp(value, 0, 2)];

    public static string Horizontal(int value) => HorizontalNames[Math.Clamp(value, 0, 2)];

    /// <summary>上下余白の欄の名前（上寄せは上余白、下寄せは下余白、中央は上下余白）。</summary>
    public static string VerticalMarginLabel(int vertical) => vertical switch { 0 => "上余白 px", 2 => "下余白 px", _ => "上下余白 px" };

    /// <summary>一覧の短い説明（「下寄せ・2 行」）。</summary>
    public static string Summary(N3LayoutSettings s) => $"{Vertical(s.VerticalAlignment)}・{s.LineCount} 行";

    /// <summary>出どころの短い名前（一覧のバッジ）。</summary>
    public static string Badge(N3LayoutOrigin origin) => origin switch
    {
        N3LayoutOrigin.Edited => "編集",
        N3LayoutOrigin.Added => "新規",
        _ => "ベース",
    };

    /// <summary>出どころの説明。</summary>
    public static string OriginNote(N3LayoutOrigin origin) => origin switch
    {
        N3LayoutOrigin.Edited => "編集済み（書き出しで、ベースの同じ名前のレイアウト設定に上書きします。「ベースの値に戻す」で戻せます）",
        N3LayoutOrigin.Added => "NicoKaraPrep で足したレイアウト設定です（書き出しで追加します）",
        _ => "ベースの n3proj（無ければ書き出しの既定）の値です。値を変えると編集済みになります",
    };

    /// <summary>ページのレイアウトの決まり方の短い名前。</summary>
    public static string Source(N3PageLayoutSource source) => source switch
    {
        N3PageLayoutSource.Manual => "手動",
        N3PageLayoutSource.TabFixed => "タブ固定",
        _ => "自動",
    };

    /// <summary>時刻 ms の表示（行リストと同じ mm:ss:cc）。</summary>
    public static string Time(int ms) => TimeTag.Format(N3ShowTimeAdjuster.ToCs(ms)).Trim('[', ']');

    /// <summary>短くした歌詞（一覧用）。</summary>
    public static string Excerpt(string text, int max = 24)
    {
        text = text.Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}

/// <summary>レイアウト設定ビューの左の一覧の 1 行（書き出しの並びのレイアウト設定 1 件）。</summary>
public sealed partial class LayoutListItem : ObservableObject
{
    public LayoutListItem(N3LayoutEntry entry, int pageCount, bool merge)
    {
        Entry = entry;
        Update(entry, pageCount, merge);
    }

    /// <summary>書き出しの並びの 1 件と出どころ。</summary>
    public N3LayoutEntry Entry { get; private set; }

    public string Name => Entry.Name;

    public N3LayoutOrigin Origin => Entry.Origin;

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string badgeText = "";

    [ObservableProperty]
    private bool isBase;

    [ObservableProperty]
    private bool isEdited;

    [ObservableProperty]
    private bool isAdded;

    /// <summary>このレイアウトを使っているページの数（全タブ）。</summary>
    [ObservableProperty]
    private int pageCount;

    [ObservableProperty]
    private string pageText = "";

    [ObservableProperty]
    private string summary = "";

    [ObservableProperty]
    private string toolTip = "";

    /// <summary>中身を新しい並び・使っているページの数に合わせる（同じ名前の行を使い回して、一覧の選択とスクロールを保つ）。</summary>
    public void Update(N3LayoutEntry entry, int pageCount, bool merge)
    {
        Entry = entry;
        var s = entry.EditingSettings;
        DisplayName = entry.Name.Length > 0 ? entry.Name : "（名前なし）";
        BadgeText = LayoutTexts.Badge(entry.Origin);
        IsBase = entry.Origin == N3LayoutOrigin.Base;
        IsEdited = entry.Origin == N3LayoutOrigin.Edited;
        IsAdded = entry.Origin == N3LayoutOrigin.Added;
        PageCount = pageCount;
        PageText = pageCount > 0 ? $"{pageCount} ページ" : "未使用";
        Summary = LayoutTexts.Summary(s);
        string notUsed = !merge && entry.Origin != N3LayoutOrigin.Base
            ? "\n（この曲は編集したレイアウト設定をベースへ反映しない設定なので、書き出しではベースの値を使います）"
            : "";
        ToolTip = $"{DisplayName}（{BadgeText}）\n{LayoutTexts.OriginNote(entry.Origin)}{notUsed}\n" +
                  $"上下配置: {LayoutTexts.Vertical(s.VerticalAlignment)}（{LayoutTexts.VerticalMarginLabel(s.VerticalAlignment).Replace(" px", "")} {s.VerticalMarginPx:0.#} px）・左右余白 {s.HorizontalMarginPx:0.#} px・行間 {s.LineSpacePx:0.#} px\n" +
                  $"行ごとの左右配置（上の行から）: {string.Join("・", s.HorizontalAlignments.Select(LayoutTexts.Horizontal))}\n" +
                  $"使っているページ: {PageText}";
    }

    /// <summary>読み上げ・UI オートメーションでの名前（一覧の行の名前になる）。</summary>
    public override string ToString() => $"{DisplayName}（{BadgeText}）";
}

/// <summary>レイアウト設定ビューの右のページの一覧の 1 行（全タブのページ 1 つ）。</summary>
public sealed partial class LayoutPageItem : ObservableObject
{
    public LayoutPageItem(LayoutPageInfo info, N3SubtitleAction songDefault)
    {
        Info = info;
        Update(info, songDefault);
    }

    /// <summary>ページの中身（タブ・行・レイアウト・アクション）。</summary>
    public LayoutPageInfo Info { get; private set; }

    /// <summary>ページを見分ける鍵（タブの名前とページの番号。一覧を作り直したあとに選び直すのに使う）。</summary>
    public string Key => PageKey(Info.TabName, Info.PageIndex);

    public static string PageKey(string tabName, int pageIndex) => $"{tabName}\n{pageIndex}";

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string lineCountText = "";

    [ObservableProperty]
    private string startText = "";

    [ObservableProperty]
    private string firstText = "";

    [ObservableProperty]
    private string layoutText = "";

    [ObservableProperty]
    private string actionText = "";

    [ObservableProperty]
    private bool isManualLayout;

    [ObservableProperty]
    private bool isManualAction;

    [ObservableProperty]
    private string toolTip = "";

    /// <summary>中身を新しいページの情報に合わせる（同じページの行を使い回して、一覧の選択を保つ）。</summary>
    public void Update(LayoutPageInfo info, N3SubtitleAction songDefault)
    {
        Info = info;
        Title = $"{info.TabName} p.{info.PageIndex + 1}";
        LineCountText = $"{info.Lines.Count} 行";
        StartText = info.StartMs is int ms ? LayoutTexts.Time(ms) : "タグなし";
        FirstText = LayoutTexts.Excerpt(info.FirstText);
        LayoutText = $"{(info.LayoutName.Length > 0 ? info.LayoutName : "（なし）")}（{LayoutTexts.Source(info.LayoutChoice.Source)}）";
        IsManualLayout = info.LayoutChoice.Source == N3PageLayoutSource.Manual;
        var a = info.Action;
        ActionText = a.State switch
        {
            N3PageActionState.Manual => $"{N3SubtitleActionCatalog.Describe(a.Action)}（手動）",
            N3PageActionState.Mixed when a.DefaultLines > 0 => $"混在（手動 {a.ManualLines} 行・既定 {a.DefaultLines} 行）",
            N3PageActionState.Mixed => $"混在（{a.ManualKinds} 種類）",
            _ => $"{N3SubtitleActionCatalog.Describe(songDefault)}（既定）",
        };
        IsManualAction = a.State != N3PageActionState.Default;
        string layoutHow = info.LayoutChoice.Source switch
        {
            N3PageLayoutSource.Manual => "このページに手動で指定したレイアウト",
            N3PageLayoutSource.TabFixed => "タブに固定したレイアウト（n3proj の書き出しの設定）",
            _ => $"ページの行数（{info.Lines.Count} 行）から自動で選んだレイアウト",
        };
        string actionHow = a.State switch
        {
            N3PageActionState.Manual => "このページに手動で指定した字幕アクション",
            N3PageActionState.Mixed => "行によって違う字幕アクション（「このページに」でそろえられます）",
            _ => "曲の既定の字幕アクション",
        };
        string where = info.IsActiveTab ? "" : "\n（表示中でないタブのページです。指定を変えたときは、そのタブを表示して Ctrl+Z で戻せます）";
        ToolTip = $"{Title}（{info.Lines.Count} 行・{info.Lines[0] + 1}〜{info.Lines[^1] + 1} 行目・歌い出し {StartText}）\n{info.FirstText}\n" +
                  $"{layoutHow}: {(info.LayoutName.Length > 0 ? info.LayoutName : "（なし）")}\n{actionHow}: {ActionText}{where}";
    }

    /// <summary>読み上げ・UI オートメーションでの名前（一覧の行の名前になる）。</summary>
    public override string ToString() => $"{Title} {LineCountText} {StartText} {LayoutText} {ActionText}";
}

/// <summary>字幕アクションの種類の選択肢（コンボの 1 項目。Id が null は「自動」・「（既定）」）。</summary>
public sealed record LayoutActionChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}
