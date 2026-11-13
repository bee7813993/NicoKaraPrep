namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// 字幕のプレビューで行をマウスでドラッグしたときの、レイアウト設定の変え方（字幕の画面の px で動かした量から、新しい値を決める）。
/// 上下: 上下の基準の行（下寄せは一番下の段、上寄せは一番上の段）は上下余白を、ほかの段は行間を変える（ドラッグした段が指の位置に付いてくるように）。
/// 中央寄せは上下余白を使わないので、行間だけを変える（真ん中の段は上下に動かない）。
/// 左右: 左寄せ・右寄せの段は左右余白を変える（左右余白は左寄せ・右寄せの段で共通）。中央の段は左右には動かない。
/// 値は整数の px に丸め、余白は 0 未満にしない（行間はマイナスも可）。
/// </summary>
public static class N3LayoutDrag
{
    /// <summary>上下の基準の段か（上下余白で位置が決まる段。下寄せは一番下、上寄せは一番上。中央寄せには無い）。</summary>
    /// <param name="row">ページの下から何段目か（1 始まり）。</param>
    /// <param name="rowsInPage">ページの段の数。</param>
    public static bool IsAnchorRow(N3LayoutSettings layout, int row, int rowsInPage) => layout.VerticalAlignment switch
    {
        2 => row == 1,
        0 => row == Math.Max(rowsInPage, row),
        _ => false,
    };

    /// <summary>
    /// 行を字幕の画面で (<paramref name="dx"/>, <paramref name="dy"/>) px 動かしたときのレイアウト設定（下・右が正）。
    /// <paramref name="start"/> はドラッグを始めたときの値。
    /// </summary>
    public static N3LayoutSettings Apply(N3LayoutSettings start, int row, int rowsInPage, double dx, double dy)
    {
        int rows = Math.Max(rowsInPage, row);
        double vm = start.VerticalMarginPx, hm = start.HorizontalMarginPx, space = start.LineSpacePx;
        switch (start.VerticalAlignment)
        {
            case 2: // 下寄せ: 段の下端 = 画面の高さ − 下余白 − (段 − 1) × (枠の高さ + 行間)
                if (row <= 1) vm = start.VerticalMarginPx - dy;
                else space = start.LineSpacePx - dy / (row - 1);
                break;
            case 0: // 上寄せ: 段の上端 = 上余白 + (段の数 − 段) × (枠の高さ + 行間)
                if (row >= rows) vm = start.VerticalMarginPx + dy;
                else space = start.LineSpacePx + dy / (rows - row);
                break;
            default: // 中央: 段の上端 = (画面の高さ − 全体の高さ) / 2 + (段の数 − 段) × (枠の高さ + 行間)
            {
                double k = (rows + 1) / 2.0 - row;
                if (rows > 1 && Math.Abs(k) > 0.01) space = start.LineSpacePx + dy / k;
                break;
            }
        }
        int align = start.AlignmentForRow(row, rows);
        if (align == 0) hm = start.HorizontalMarginPx + dx;
        else if (align == 2) hm = start.HorizontalMarginPx - dx;

        return start with
        {
            VerticalMarginPx = Math.Max(0, Math.Round(vm)),
            HorizontalMarginPx = Math.Max(0, Math.Round(hm)),
            LineSpacePx = Math.Round(space),
        };
    }

    /// <summary>ドラッグで変わる値の名前（「上下余白・左右余白」「行間」など。何も変わらない段は空）。</summary>
    public static string Targets(N3LayoutSettings layout, int row, int rowsInPage)
    {
        int rows = Math.Max(rowsInPage, row);
        var parts = new List<string>();
        if (IsAnchorRow(layout, row, rows)) parts.Add(layout.VerticalAlignment == 0 ? "上余白" : "下余白");
        else if (layout.VerticalAlignment is 0 or 2 ? rows > 1 : rows > 1 && Math.Abs((rows + 1) / 2.0 - row) > 0.01) parts.Add("行間");
        if (layout.AlignmentForRow(row, rows) is 0 or 2) parts.Add("左右余白");
        return string.Join("・", parts);
    }

    /// <summary>今の値の説明（ドラッグ中にプレビューに出す。「下余白 80 px・左右余白 50 px・行間 60 px」）。</summary>
    public static string Describe(N3LayoutSettings layout)
    {
        string vertical = layout.VerticalAlignment switch { 0 => $"上余白 {layout.VerticalMarginPx:0} px・", 2 => $"下余白 {layout.VerticalMarginPx:0} px・", _ => "" };
        return $"{vertical}左右余白 {layout.HorizontalMarginPx:0} px・行間 {layout.LineSpacePx:0} px";
    }

    /// <summary>
    /// 変えた値の説明（「下余白 50 → 80 px・行間 60 → 72 px」。変わっていなければ空）。
    /// </summary>
    public static string DescribeChange(N3LayoutSettings before, N3LayoutSettings after)
    {
        var parts = new List<string>();
        string vName = before.VerticalAlignment == 0 ? "上余白" : "下余白";
        if (before.VerticalMarginPx != after.VerticalMarginPx) parts.Add($"{vName} {before.VerticalMarginPx:0} → {after.VerticalMarginPx:0} px");
        if (before.HorizontalMarginPx != after.HorizontalMarginPx) parts.Add($"左右余白 {before.HorizontalMarginPx:0} → {after.HorizontalMarginPx:0} px");
        if (before.LineSpacePx != after.LineSpacePx) parts.Add($"行間 {before.LineSpacePx:0} → {after.LineSpacePx:0} px");
        return string.Join("・", parts);
    }
}
