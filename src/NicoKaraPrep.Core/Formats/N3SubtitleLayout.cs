using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// ニコカラメーカー3 のレイアウト設定 1 件（字幕の位置。LyricsLayoutModel）。px はプロジェクトの画面の高さでの値。
/// 上下配置 0 = 上寄せ / 1 = 中央 / 2 = 下寄せ。行ごとの左右配置 0 = 左寄せ / 1 = 中央 / 2 = 右寄せ（上の行から）。
/// スマート水平配置 0 = 調整しない / 1 = 中心位置揃え / 2 = 左右余白揃え。ルビ配置 0 = 自動 / 1 = 中央 / 2 = 均等割り付け。
/// </summary>
public sealed record N3LayoutSettings(
    string Name,
    int Index,
    int VerticalAlignment,
    double LineSpacePx,
    double VerticalMarginPx,
    double HorizontalMarginPx,
    IReadOnlyList<int> HorizontalAlignments,
    int SmartHorizon,
    double LyricsIntervalPx,
    double RubyIntervalPx,
    double LyricsAndRubyIntervalPx,
    int RubyAlignment,
    bool AllowBiting = false)
{
    /// <summary>行ごとの左右配置の行数（レイアウトを選ぶときのページの行数）。</summary>
    public int LineCount => Math.Max(1, HorizontalAlignments.Count);

    /// <summary>書き出しのレイアウトの選び方（<see cref="N3ProjWriter.LayoutResolver"/>）に渡す形。</summary>
    public N3ProjLayoutInfo Info => new(Name, Index, LineCount);

    /// <summary>
    /// ページの下から <paramref name="row"/> 行目（1 から）の左右配置。上寄せ・中央は上から数え、下寄せは下から数える
    /// （設定より行が多ければ、上端（下寄せは上端、上寄せは下端）の配置を使う）。
    /// </summary>
    public int AlignmentForRow(int row, int rowsInPage)
    {
        if (HorizontalAlignments.Count == 0) return 1;
        int count = HorizontalAlignments.Count;
        int index = VerticalAlignment == 2
            ? count - row                    // 下寄せ: 下の行から設定の下の行に合わせる
            : Math.Max(1, rowsInPage) - row; // 上寄せ・中央: 上の行から
        return HorizontalAlignments[Math.Clamp(index, 0, count - 1)];
    }
}

/// <summary>ニコカラメーカー3 のプロジェクトのレイアウト設定（LyricsLayouts）を読む。</summary>
public static class N3LayoutReader
{
    /// <summary>プロジェクトの JSON からレイアウト設定を読む（px は <paramref name="height"/> での値）。</summary>
    public static List<N3LayoutSettings> Read(JsonObject root, int height)
    {
        var result = new List<N3LayoutSettings>();
        if (root["LyricsLayouts"] is not JsonArray layouts) return result;
        int index = 0;
        foreach (var node in layouts)
        {
            if (node is JsonObject o) result.Add(ParseLayout(o, index, height));
            index++;
        }
        return result;
    }

    /// <summary>
    /// レイアウト設定（LyricsLayoutModel）1 件を読む。n3proj の LyricsLayouts の 1 件と、レイアウト設定テンプレート（.tpl）の中身で共通。
    /// px は <paramref name="height"/> での値、<paramref name="index"/> はレイアウト設定の並びの番号。
    /// </summary>
    public static N3LayoutSettings ParseLayout(JsonObject o, int index, int height)
    {
        var aligns = new List<int>();
        if (o["HorizontalAlignments"] is JsonArray ha)
        {
            foreach (var a in ha) aligns.Add(N3FontJson.Int(a?["HorizontalLayoutAlignment"]) ?? 0);
        }
        return new N3LayoutSettings(
            o["SettingsName"]?.GetValue<string>() ?? "",
            index,
            N3FontJson.Int(o["SelectedVerticalAlignmentIndex"]) ?? 2,
            N3FontJson.SizePx(o["LineSpace"], height),
            N3FontJson.SizePx(o["VerticalMargin"], height),
            N3FontJson.SizePx(o["HorizontalMargin"], height),
            aligns,
            N3FontJson.Int(o["SmartHorizon"]) ?? 0,
            N3FontJson.SizePx(o["LyricsInterval"], height),
            N3FontJson.SizePx(o["RubyInterval"], height),
            N3FontJson.SizePx(o["LyricsAndRubyInterval"], height),
            N3FontJson.Int(o["RubyAlignment"]) ?? 0,
            N3FontJson.Bool(o["AllowBiting"]) ?? false);
    }

    /// <summary>プロジェクトの画面（背景素材）の幅・高さ px（無ければ 1920 × 1080。幅が無ければ高さの 16:9）。</summary>
    public static (int Width, int Height) ScreenSize(JsonObject root)
    {
        int height = N3FontJson.Int(root["SourceInfo"]?["BackgroundHeight"]) ?? 1080;
        if (height <= 0) height = 1080;
        int width = N3FontJson.Int(root["SourceInfo"]?["BackgroundWidth"]) ?? 0;
        if (width <= 0) width = (int)Math.Round(height * 16.0 / 9);
        return (width, height);
    }

    /// <summary>ベースの n3proj が無いときに書き出すレイアウト（下寄せ1行・下寄せ2行など）。</summary>
    public static List<N3LayoutSettings> Defaults(int height) =>
        Read(new JsonObject { ["LyricsLayouts"] = N3ProjWriter.NewDefaultLayouts(height, N3ProjWriter.DefaultAppVersion) }, height);
}

/// <summary>
/// 行の文字のワイプの時刻。タイムタグからタイムタグまでの文字を 1 つのまとまりとして、そのあいだに左から右へワイプする
/// （書き出しの文字の時刻と同じ区切り。行頭のタグの無い文字は最初のタグの時刻に一度にワイプする。2 連タグの最初のタグはまとまりの終わり）。
/// </summary>
public static class N3WipeTimeline
{
    /// <summary>ワイプのまとまり 1 つ（CharUnit の添字の範囲、両端を含む。時刻は 10ms 単位）。</summary>
    public readonly record struct Group(int FirstUnit, int LastUnit, int StartCs, int EndCs);

    /// <summary>行のワイプのまとまり（表示文字を含むものだけ。文字の順）。</summary>
    public static List<Group> Groups(LyricsLine line)
    {
        var result = new List<Group>();
        var chars = line.Chars;
        int? firstTag = chars.FirstOrDefault(c => c.TimeCs is not null)?.TimeCs ?? line.EndTimeCs;
        if (firstTag is not int first) return result;

        // 行頭のタグの無い文字: 最初のタグの時刻に一度にワイプする
        int i = 0, leadStart = -1, leadLast = -1;
        for (; i < chars.Count && chars[i].TimeCs is null; i++)
        {
            if (chars[i].IsSpacer) continue;
            if (leadStart < 0) leadStart = i;
            leadLast = i;
        }
        if (leadStart >= 0) result.Add(new Group(leadStart, leadLast, first, first));

        // タグから次のタグまで
        int groupStart = -1, groupLast = -1, startCs = first;
        for (; i < chars.Count; i++)
        {
            var c = chars[i];
            if (c.TimeCs is int t)
            {
                if (groupStart >= 0) result.Add(new Group(groupStart, groupLast, startCs, t));
                groupStart = -1;
                startCs = t;
                if (c.IsSpacer) continue; // 2 連タグの最初のタグ: まとまりの終わり
                groupStart = i;
                groupLast = i;
                continue;
            }
            if (c.IsSpacer) continue;
            if (groupStart < 0) groupStart = i;
            groupLast = i;
        }
        if (groupStart >= 0) result.Add(new Group(groupStart, groupLast, startCs, line.EndTimeCs ?? startCs));
        return result;
    }

    /// <summary>まとまりの時刻 <paramref name="cs"/> でのワイプの進み具合（0〜1）。終わりが始まり以前ならその時刻に一度にワイプする。</summary>
    public static double Progress(Group group, double cs)
    {
        if (cs < group.StartCs) return 0;
        if (group.EndCs <= group.StartCs) return 1;
        return Math.Clamp((cs - group.StartCs) / (group.EndCs - group.StartCs), 0, 1);
    }
}
