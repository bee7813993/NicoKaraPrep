using CommunityToolkit.Mvvm.ComponentModel;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 左の一覧で複数のフォント設定を選んで、組み合わせのフォント設定を作る（選ぶ状態）。押した順が上の帯からの並びになり、
/// 一覧の行に順番を出す。もう一度押すと外す。選び終えたら、組み合わせを作る画面へ並びを渡す。
/// </summary>
public sealed partial class FontSettingsViewModel
{
    /// <summary>選んだフォント設定の Id（押した順）。</summary>
    private readonly List<string> _picks = new();

    /// <summary>選ぶ状態を始める最中か（そのあいだの <see cref="IsPicking"/> の変化では、選んだものを入れ直さない）。</summary>
    private bool _startingPick;

    /// <summary>選ぶ状態をやめたことをステータスバーに出さないか（組み合わせを作ったあとなど）。</summary>
    private bool _quietPickChange;

    /// <summary>組み合わせるフォント設定を一覧で選んでいる最中か。</summary>
    [ObservableProperty]
    private bool isPicking;

    /// <summary>選んだ順の説明（「（梢） → （吟子）」など）。</summary>
    [ObservableProperty]
    private string pickText = "";

    /// <summary>2 つ以上選んで、組み合わせを作れるか。</summary>
    [ObservableProperty]
    private bool canComposePicks;

    /// <summary>選んだフォント設定（押した順。一覧から無くなったものは除く）。</summary>
    public List<FontListItem> PickedItems =>
        _picks.Select(id => _all.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))
            .OfType<FontListItem>()
            .ToList();

    /// <summary>一覧の下のボタンで選ぶ状態を切り替えたとき: 始めるなら今のフォント設定から選び、やめるなら選んだものを外す。</summary>
    partial void OnIsPickingChanged(bool value)
    {
        if (_startingPick) return;
        _picks.Clear();
        if (value && SelectedItem is { } current) _picks.Add(current.Id);
        RefreshPicks();
        if (!_quietPickChange) SetStatus(value ? PickGuide : "組み合わせるフォント設定を選ぶのをやめました");
    }

    private const string PickGuide = "組み合わせるフォント設定を、上の帯にする順に一覧で押してください（もう一度押すと外します。Esc でやめます）";

    /// <summary>選ぶ状態を始める（<paramref name="first"/> があれば、それを 1 つ目に選ぶ）。</summary>
    public void StartPicking(FontListItem? first)
    {
        _startingPick = true;
        try
        {
            IsPicking = true;
        }
        finally
        {
            _startingPick = false;
        }
        _picks.Clear();
        if (first is not null) _picks.Add(first.Id);
        RefreshPicks();
        SetStatus(PickGuide);
    }

    /// <summary>一覧のフォント設定を選ぶ（選んでいれば外す）。選ぶ状態でなければ何もしない。</summary>
    public void TogglePick(FontListItem item)
    {
        if (!IsPicking) return;
        int i = _picks.FindIndex(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _picks.RemoveAt(i);
        else _picks.Add(item.Id);
        RefreshPicks();
    }

    /// <summary>選ぶ状態をやめる（選んだものを外す）。quiet ならステータスバーに出さない。</summary>
    public void StopPicking(bool quiet = false)
    {
        _quietPickChange = quiet;
        try
        {
            IsPicking = false;
        }
        finally
        {
            _quietPickChange = false;
        }
    }

    /// <summary>一覧の行の順番の表示と、選んだ順の説明を読み直す（一覧を作り直したあとも）。</summary>
    private void RefreshPicks()
    {
        _picks.RemoveAll(id => !_all.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)));
        foreach (var item in _all)
        {
            item.PickOrder = IsPicking ? _picks.FindIndex(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)) + 1 : 0;
        }
        var names = PickedItems.Select(i => i.Font.Name).ToList();
        PickText = names.Count == 0 ? "まだ選んでいません" : string.Join(" → ", names);
        CanComposePicks = IsPicking && names.Count >= 2;
    }
}
