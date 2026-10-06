using System.Text.Json;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// NicoKaraPrep のレイアウト設定（アプリ共通の一覧 <see cref="Project.AppSettings.N3Layouts"/>）の 元に戻す・やり直し の履歴
/// （レイアウト設定ビューで使う。フォント設定ビューと同じ形）。
/// 操作の前に一覧の写しを積み、名前の変更で行の参照を付け替えたときは、その名前の組も記録に添える（戻すときに逆向きに付け替える）。
/// 上限は <see cref="MaxEntries"/> 回。同じ欄の続けての変更は <see cref="CoalesceWindow"/> 以内なら 1 回にまとめる。
/// </summary>
public sealed class N3LayoutHistory
{
    /// <summary>元に戻す の上限。</summary>
    public const int MaxEntries = 100;

    /// <summary>同じ欄の続けての変更（数値の欄のスピンなど）を 1 回の 元に戻す にまとめる間隔。</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>記録 1 回分（その操作の前の一覧の写しと、その操作で行の参照を付け替えた名前の組）。</summary>
    public sealed class Entry
    {
        internal Entry(List<N3Layout> layouts)
        {
            Layouts = layouts;
        }

        /// <summary>操作の前の一覧（写し）。</summary>
        public IReadOnlyList<N3Layout> Layouts { get; }

        /// <summary>この操作で行・タブの参照を付け替えた名前（変更前, 変更後）。操作の順。</summary>
        public List<(string Old, string New)> Renames { get; } = new();
    }

    /// <summary>戻す・やり直すときに当てるもの。</summary>
    /// <param name="Layouts">一覧をこの写しにする（呼び出し側のものにしてよい新しい写し）。</param>
    /// <param name="ReferenceChanges">行・タブの参照を付け替える名前（From → To。この順に当てる）。</param>
    public sealed record Change(List<N3Layout> Layouts, IReadOnlyList<(string From, string To)> ReferenceChanges);

    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private string? _coalesceKey;
    private DateTime _lastEditUtc;

    /// <summary>戻せる操作があるか。</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>やり直せる操作があるか。</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>戻せる操作の数。</summary>
    public int UndoCount => _undo.Count;

    /// <summary>やり直せる操作の数。</summary>
    public int RedoCount => _redo.Count;

    /// <summary>
    /// 変更の前に今の一覧を記録する。<paramref name="key"/> が前回と同じで <see cref="CoalesceWindow"/> 以内なら（同じ欄の続けての変更）、
    /// 前回の記録にまとめてそれを返す（<paramref name="added"/> は false）。key が null の操作（追加・削除など）は必ず 1 回分として記録する。
    /// 記録したらやり直しの履歴は捨てる。
    /// </summary>
    public Entry Push(IEnumerable<N3Layout> current, string? key, DateTime nowUtc, out bool added)
    {
        if (key is not null && key == _coalesceKey && nowUtc - _lastEditUtc < CoalesceWindow && _undo.Count > 0)
        {
            _lastEditUtc = nowUtc;
            added = false;
            return _undo[^1];
        }

        var entry = new Entry(Copy(current));
        _undo.Add(entry);
        if (_undo.Count > MaxEntries) _undo.RemoveAt(0);
        _redo.Clear();
        _coalesceKey = key;
        _lastEditUtc = nowUtc;
        added = true;
        return entry;
    }

    /// <summary>何も変わらなかった操作の記録を取り消す（最後の記録が <paramref name="entry"/> のときだけ）。</summary>
    public void Drop(Entry entry)
    {
        if (_undo.Count > 0 && ReferenceEquals(_undo[^1], entry)) _undo.RemoveAt(_undo.Count - 1);
        _coalesceKey = null;
    }

    /// <summary>次の変更を前の記録にまとめない（選ぶレイアウトを変えた・元に戻したなど）。</summary>
    public void BreakCoalescing() => _coalesceKey = null;

    /// <summary>履歴をすべて捨てる（ビューの外で一覧が変わったとき）。</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _coalesceKey = null;
    }

    /// <summary>
    /// 1 つ戻す。今の一覧をやり直しの履歴に積み（戻す記録の名前の組を添える）、戻す先の一覧と、行の参照を逆向きに付け替える名前の組
    /// （後の変更から順に 変更後 → 変更前）を返す。戻せなければ null。
    /// </summary>
    public Change? Undo(IEnumerable<N3Layout> current)
    {
        if (_undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var now = new Entry(Copy(current));
        now.Renames.AddRange(entry.Renames);
        _redo.Add(now);
        _coalesceKey = null;
        var changes = entry.Renames.AsEnumerable().Reverse().Select(r => (From: r.New, To: r.Old)).ToList();
        return new Change(Copy(entry.Layouts), changes);
    }

    /// <summary>
    /// 戻した操作を 1 つやり直す。今の一覧を元に戻すの履歴に積み、やり直した後の一覧と、行の参照を付け替える名前の組
    /// （操作の順に 変更前 → 変更後）を返す。やり直せなければ null。
    /// </summary>
    public Change? Redo(IEnumerable<N3Layout> current)
    {
        if (_redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var now = new Entry(Copy(current));
        now.Renames.AddRange(entry.Renames);
        _undo.Add(now);
        if (_undo.Count > MaxEntries) _undo.RemoveAt(0);
        _coalesceKey = null;
        var changes = entry.Renames.Select(r => (From: r.Old, To: r.New)).ToList();
        return new Change(Copy(entry.Layouts), changes);
    }

    /// <summary>一覧の深い写し（行ごとの左右配置の並びも写す。識別子は同じ）。</summary>
    public static List<N3Layout> Copy(IEnumerable<N3Layout> layouts) => layouts.Select(l => l.Clone()).ToList();

    /// <summary>一覧の中身を比べるための文字列（ビューの外で一覧が変わったかを調べる）。</summary>
    public static string Signature(IEnumerable<N3Layout> layouts) => JsonSerializer.Serialize(layouts.ToList());
}
