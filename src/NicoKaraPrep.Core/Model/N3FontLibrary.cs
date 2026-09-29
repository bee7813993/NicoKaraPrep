using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Model;

/// <summary>フォント設定の検証結果の種類。</summary>
public enum N3FontIssueKind
{
    /// <summary>名前が空。</summary>
    EmptyName,

    /// <summary>名前が重複している。</summary>
    DuplicateName,

    /// <summary>行が参照しているフォント設定が無い。</summary>
    MissingName,

    /// <summary>画像ブラシの画像が無い。</summary>
    MissingBitmap,

    /// <summary>マーカーの位置が 0–1 の範囲外。</summary>
    StopOutOfRange,

    /// <summary>0% または 100% の位置にマーカーが重なっている。</summary>
    StopOverlap,

    /// <summary>塗りの種類・不透明度・文字飾り・ブラーの濃さが範囲外。</summary>
    ValueOutOfRange,
}

/// <summary>フォント設定の検証結果 1 件。</summary>
/// <param name="Kind">種類。</param>
/// <param name="Severity">重要度。</param>
/// <param name="Message">表示メッセージ。</param>
/// <param name="FontId">対象のフォント設定の <see cref="N3FontSet.Id"/>（行の参照切れは null）。</param>
/// <param name="FontName">対象のフォント設定名（行の参照切れは参照している名前）。</param>
/// <param name="BrushIndex">対象の配色の箇所（無ければ -1）。</param>
public sealed record N3FontIssue(
    N3FontIssueKind Kind,
    IssueSeverity Severity,
    string Message,
    string? FontId = null,
    string? FontName = null,
    int BrushIndex = -1);

/// <summary>
/// フォント設定の一覧（アプリ共通のライブラリ・曲専用）に対する操作。
/// 一覧を直接変更する操作と、値を計算するだけの関数からなる。行の参照の更新・保存・Undo は呼び出し側で行う。
/// </summary>
public static class N3FontLibrary
{
    /// <summary>名前が空のときに付ける名前（ニコカラメーカーと同じ）。</summary>
    public const string NewFontName = "新規";

    /// <summary>ニコカラメーカーのデフォルトフォント（歌詞／漢字が継承のときの値。フォント名は環境で決まるため空のまま）。</summary>
    public static N3FontFace NkmDefaultFace() => new()
    {
        FontName = "",
        FaceName = "Bold",
        SizePx = 100,
        XScale = 100,
        EdgePx = 5,
        UseEdge2 = false,
        Edge2Px = 5,
    };

    // ------------------------------------------------------------ 一覧の操作

    /// <summary>Id が重複しているフォント設定に新しい Id を付ける（空の Id は参照時に付与される）。</summary>
    public static void EnsureIds(IList<N3FontSet> list)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in list)
        {
            while (!seen.Add(f.Id)) f.Id = Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// 一覧の中で重ならない名前を返す（ニコカラメーカーの AdjustSettingName と同じ規則）。
    /// 空なら「新規」、重なるなら末尾の半角数字を外して 2, 3… を付ける。<paramref name="except"/> は比較から外す。
    /// </summary>
    public static string UniqueName(IEnumerable<N3FontSet> list, string? name, N3FontSet? except = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = NewFontName;
        var names = new HashSet<string>(list.Where(f => !ReferenceEquals(f, except)).Select(f => f.Name), StringComparer.Ordinal);
        if (!names.Contains(name)) return name;

        int digits = 0;
        while (digits < name.Length && char.IsAsciiDigit(name[name.Length - 1 - digits])) digits++;
        string stem = name[..^digits];
        int suffix = digits > 0 && int.TryParse(name[^digits..], out int n) ? n : 0;
        if (suffix < 2) suffix = 2;
        while (names.Contains(stem + suffix)) suffix++;
        return stem + suffix;
    }

    /// <summary>フォント設定を末尾（または <paramref name="index"/> の位置）に追加する。名前と Id は一覧の中で重ならないようにする。</summary>
    public static N3FontSet Add(IList<N3FontSet> list, N3FontSet fontSet, int? index = null)
    {
        fontSet.Name = UniqueName(list, fontSet.Name, fontSet);
        if (list.Any(f => !ReferenceEquals(f, fontSet) && string.Equals(f.Id, fontSet.Id, StringComparison.OrdinalIgnoreCase)))
        {
            fontSet.Id = Guid.NewGuid().ToString();
        }
        int at = index is int i ? Math.Clamp(i, 0, list.Count) : list.Count;
        list.Insert(at, fontSet);
        return fontSet;
    }

    /// <summary>Id でフォント設定を探す。</summary>
    public static N3FontSet? Find(IEnumerable<N3FontSet> list, string id) =>
        list.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// フォント設定を複製して元の直後に入れる（名前は末尾に 2, 3… を付ける）。
    /// 複製は別のフォントなので、ニコカラメーカーの Guid・テンプレート連動・取り込み元は引き継がない。見つからなければ null。
    /// </summary>
    public static N3FontSet? Duplicate(IList<N3FontSet> list, string id)
    {
        int i = IndexOf(list, id);
        if (i < 0) return null;
        var copy = list[i].Clone();
        copy.Id = Guid.NewGuid().ToString();
        copy.Name = UniqueName(list, list[i].Name);
        copy.NkmGuid = null;
        copy.NkmSynchronize = false;
        copy.ImportedUtc = null;
        copy.ImportedFrom = null;
        list.Insert(i + 1, copy);
        return copy;
    }

    /// <summary>フォント設定を削除する。見つからなければ false。</summary>
    public static bool Remove(IList<N3FontSet> list, string id)
    {
        int i = IndexOf(list, id);
        if (i < 0) return false;
        list.RemoveAt(i);
        return true;
    }

    /// <summary>フォント設定を <paramref name="newIndex"/> の位置へ移す（範囲外は端へ）。見つからなければ false。</summary>
    public static bool Move(IList<N3FontSet> list, string id, int newIndex)
    {
        int i = IndexOf(list, id);
        if (i < 0) return false;
        var f = list[i];
        list.RemoveAt(i);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), f);
        return true;
    }

    /// <summary>
    /// フォント設定の名前を変える（他と重なれば末尾に 2, 3… を付ける）。名前が変わったら編集扱い（<see cref="MarkEdited"/>）にする。
    /// 戻り値は変更前の名前（見つからなければ null）。行の <c>FontSetName</c> の参照は呼び出し側で更新する。
    /// </summary>
    public static string? Rename(IList<N3FontSet> list, string id, string newName)
    {
        var f = Find(list, id);
        if (f is null) return null;
        string old = f.Name;
        string name = UniqueName(list, newName, f);
        if (name != old)
        {
            f.Name = name;
            MarkEdited(f);
        }
        return old;
    }

    /// <summary>NicoKaraPrep で編集したことを記録する（書き出し時にニコカラメーカーのテンプレート連動を外す）。</summary>
    public static void MarkEdited(N3FontSet fontSet) => fontSet.NkmSynchronize = false;

    /// <summary>
    /// 書き出しに使うフォント設定。曲専用のフォントは同じ名前のアプリ共通のフォントを置き換え（位置は共通の位置）、
    /// 曲専用にしか無い名前は末尾に足す。要素は複製しない。
    /// </summary>
    public static List<N3FontSet> ResolveForExport(IReadOnlyList<N3FontSet> library, IReadOnlyList<N3FontSet> songFonts)
    {
        var songByName = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var s in songFonts)
        {
            songByName.TryAdd(s.Name, s);
        }
        var used = new HashSet<N3FontSet>(ReferenceEqualityComparer.Instance);
        var result = new List<N3FontSet>(library.Count + songFonts.Count);
        foreach (var f in library)
        {
            if (songByName.TryGetValue(f.Name, out var song))
            {
                result.Add(song);
                used.Add(song);
            }
            else
            {
                result.Add(f);
            }
        }
        result.AddRange(songFonts.Where(s => !used.Contains(s)));
        return result;
    }

    // ------------------------------------------------------------ 実効値

    /// <summary>
    /// 文字種別フォントの実効値（継承をたどった値）。歌詞／漢字はデフォルトフォント（<see cref="NkmDefaultFace"/>）を、
    /// 歌詞／かな・英数とルビ／漢字は歌詞／漢字を、ルビ／かな・英数はルビ／漢字を継承する。
    /// ルビ／漢字のサイズ・縁・縁 2 の 0 は歌詞／漢字の半分（NicoKaraPrep の書き出しの仕様）。
    /// </summary>
    public static N3FontFace EffectiveFace(N3FontSet fontSet, int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= N3FontDetail.FaceCount) throw new ArgumentOutOfRangeException(nameof(faceIndex));
        var faces = fontSet.Detail.Faces;
        var lyric = Inherit(faces[0], NkmDefaultFace(), halfSizes: false);
        return faceIndex switch
        {
            0 => lyric,
            1 or 2 => Inherit(faces[faceIndex], lyric, halfSizes: false),
            3 => Inherit(faces[3], lyric, halfSizes: true),
            _ => Inherit(faces[faceIndex], Inherit(faces[3], lyric, halfSizes: true), halfSizes: false),
        };
    }

    private static N3FontFace Inherit(N3FontFace face, N3FontFace parent, bool halfSizes)
    {
        double Size(double own, double inherited) => own > 0 ? own : (halfSizes ? inherited / 2 : inherited);
        return new N3FontFace
        {
            FontName = face.FontName.Length > 0 ? face.FontName : parent.FontName,
            FaceName = face.FaceName.Length > 0 ? face.FaceName : parent.FaceName,
            SizePx = Size(face.SizePx, parent.SizePx),
            XScale = face.XScale > 0 ? face.XScale : parent.XScale,
            EdgePx = Size(face.EdgePx, parent.EdgePx),
            UseEdge2 = face.UseEdge2 ?? parent.UseEdge2,
            Edge2Px = Size(face.Edge2Px, parent.Edge2Px),
        };
    }

    // ------------------------------------------------------------ 検証

    /// <summary>
    /// フォント設定の一覧を検証する。名前の重複・空の名前、<paramref name="referencedNames"/>（行などが参照している名前）のうち一覧に無いもの、
    /// 画像ブラシの画像が無い、マーカーの位置が 0–1 の範囲外・0% / 100% に重なる、塗りの種類などの値が範囲外、を報告する。
    /// </summary>
    public static List<N3FontIssue> Validate(IReadOnlyList<N3FontSet> list, IReadOnlyCollection<string> referencedNames)
    {
        var issues = new List<N3FontIssue>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var reportedDuplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in list)
        {
            string name = f.Name ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new N3FontIssue(N3FontIssueKind.EmptyName, IssueSeverity.Warning,
                    "名前が空のフォント設定があります（書き出されません）", f.Id, name));
            }
            else if (!names.Add(name) && reportedDuplicates.Add(name))
            {
                issues.Add(new N3FontIssue(N3FontIssueKind.DuplicateName, IssueSeverity.Warning,
                    $"フォント設定「{name}」が重複しています（書き出しでは後のものが前のものを上書きします）", f.Id, name));
            }
            ValidateDetail(f, issues);
        }

        foreach (string r in referencedNames.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(r) || names.Contains(r)) continue;
            issues.Add(new N3FontIssue(N3FontIssueKind.MissingName, IssueSeverity.Warning,
                $"行が参照しているフォント設定「{r}」がありません", null, r));
        }
        return issues;
    }

    private static void ValidateDetail(N3FontSet f, List<N3FontIssue> issues)
    {
        var d = f.Detail;
        string name = f.Name ?? "";
        void Add(N3FontIssueKind kind, IssueSeverity severity, string message, int brush = -1) =>
            issues.Add(new N3FontIssue(kind, severity, $"「{name}」: {message}", f.Id, name, brush));

        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            var b = d.Brushes[i];
            string label = N3FontDetail.BrushLabels[i];
            if (b.Type is < N3Brush.TypeSolid or > N3Brush.TypeBitmap)
            {
                Add(N3FontIssueKind.ValueOutOfRange, IssueSeverity.Error, $"{label}の塗りの種類が範囲外です（{b.Type}）", i);
            }
            if (b.AlphaPercent is < 0 or > 100)
            {
                Add(N3FontIssueKind.ValueOutOfRange, IssueSeverity.Error, $"{label}の不透明度が範囲外です（{b.AlphaPercent}%）", i);
            }
            if (b.Type == N3Brush.TypeBitmap)
            {
                if (string.IsNullOrWhiteSpace(b.BitmapPath))
                {
                    Add(N3FontIssueKind.MissingBitmap, IssueSeverity.Warning, $"{label}の画像が指定されていません", i);
                }
                else if (!File.Exists(b.BitmapPath))
                {
                    Add(N3FontIssueKind.MissingBitmap, IssueSeverity.Warning, $"{label}の画像が見つかりません（{b.BitmapPath}）", i);
                }
            }
            if (b.Type is N3Brush.TypeGradient or N3Brush.TypeMilleFeuille)
            {
                if (b.Stops.Any(s => s.Position < 0 || s.Position > 1))
                {
                    Add(N3FontIssueKind.StopOutOfRange, IssueSeverity.Error, $"{label}のマーカーの位置が 0〜100% の範囲外です", i);
                }
                if (b.Stops.Count(s => Math.Abs(s.Position) < 1e-9) > 1 || b.Stops.Count(s => Math.Abs(s.Position - 1) < 1e-9) > 1)
                {
                    Add(N3FontIssueKind.StopOverlap, IssueSeverity.Info, $"{label}のマーカーが 0% または 100% の位置に重なっています", i);
                }
                if (b.Stops.Any(s => s.AlphaPercent is < 0 or > 100))
                {
                    Add(N3FontIssueKind.ValueOutOfRange, IssueSeverity.Error, $"{label}のマーカーの不透明度が範囲外です", i);
                }
            }
        }
        if (d.DecorKind is < 0 or > 2)
        {
            Add(N3FontIssueKind.ValueOutOfRange, IssueSeverity.Error, $"文字飾りの種類が範囲外です（{d.DecorKind}）");
        }
        if (d.BlurLevel is < 0 or > 2)
        {
            Add(N3FontIssueKind.ValueOutOfRange, IssueSeverity.Error, $"ブラーの濃さが範囲外です（{d.BlurLevel}）");
        }
    }

    // ------------------------------------------------------------ 配色の一括操作

    /// <summary>ワイプ前後の配色を交換する（ニコカラメーカーの「ワイプ前後の配色を交換」）。</summary>
    public static void SwapBeforeAfter(N3FontDetail detail)
    {
        var b = detail.Brushes;
        for (int i = 0; i < N3FontDetail.BeforeOffset; i++)
        {
            (b[i], b[i + N3FontDetail.BeforeOffset]) = (b[i + N3FontDetail.BeforeOffset], b[i]);
        }
    }

    /// <summary>ワイプ後の配色をワイプ前にコピーする（ニコカラメーカーの「ワイプ後の配色をワイプ前にコピー」）。</summary>
    public static void CopyAfterToBefore(N3FontDetail detail)
    {
        var b = detail.Brushes;
        for (int i = 0; i < N3FontDetail.BeforeOffset; i++) b[i + N3FontDetail.BeforeOffset] = b[i].Clone();
    }

    /// <summary>ワイプ前の配色をワイプ後にコピーする（ニコカラメーカーの「ワイプ前の配色をワイプ後にコピー」）。</summary>
    public static void CopyBeforeToAfter(N3FontDetail detail)
    {
        var b = detail.Brushes;
        for (int i = 0; i < N3FontDetail.BeforeOffset; i++) b[i] = b[i + N3FontDetail.BeforeOffset].Clone();
    }

    /// <summary>他のフォント設定の配色を、指定した箇所（Brushes の添字）だけ写す。範囲外の添字は無視する。</summary>
    public static void CopyBrushes(N3FontDetail from, N3FontDetail to, IEnumerable<int> indices)
    {
        foreach (int i in indices.Distinct())
        {
            if (i < 0 || i >= N3FontDetail.BrushCount) continue;
            to.Brushes[i] = from.Brushes[i].Clone();
        }
    }

    private static int IndexOf(IList<N3FontSet> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
