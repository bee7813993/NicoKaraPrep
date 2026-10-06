using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>曲の既定の字幕アクションの今の状態（中央の「字幕アクションの既定（曲全体）」のカードに出すもの）。</summary>
/// <param name="IsAuto">自動か（曲の既定を決めていない）。</param>
/// <param name="Action">使うアクション（自動なら自動で決めたもの。写し）。</param>
/// <param name="AutoSource">自動のときの出どころ（自動でなくても、自動にしたら何になるか）。</param>
/// <param name="AutoLabel">コンボの「自動」の項目の文字（「（自動: ベースのまま → 文字単位フェード）」）。</param>
/// <param name="AutoNote">自動の決め方の説明。</param>
public sealed record SongActionState(bool IsAuto, N3SubtitleAction Action, N3SubtitleActionSource AutoSource, string AutoLabel, string AutoNote);

/// <summary>
/// レイアウト設定ビューの「字幕アクションの既定（曲全体）」: 行ごとの指定が無い歌詞行に書く字幕アクション（<see cref="Core.Project.N3ProjSongSettings.SubtitleAction"/>。
/// null = 自動）。変えるとすぐ .tttproj に保存する（このビューの 元に戻す の対象にしない）。
/// </summary>
public sealed partial class LayoutViewModel
{
    /// <summary>曲の既定の字幕アクションの今の状態。</summary>
    public SongActionState SongAction { get; private set; } = null!;

    /// <summary>
    /// 曲の既定の字幕アクションが変わった（引数は、種類（自動か・Id）が変わったか。変わっていれば設定欄を作り直し、値だけなら欄はそのまま）。
    /// </summary>
    public event EventHandler<bool>? SongActionChanged;

    /// <summary>前に知らせた種類（自動か・Id。設定欄を作り直すかの判断に使う）。</summary>
    private string? _songActionKind;

    /// <summary>「ニコカラメーカー3 の既定値に戻す」を押せるか（種類を選んでいて、値がその既定値と違う）。</summary>
    [ObservableProperty]
    private bool canResetSongAction;

    /// <summary>カードの説明（自動の決め方・保存先）。</summary>
    [ObservableProperty]
    private string songActionNote = "";

    /// <summary>カードの見出しの横に出す今の値（「文字単位フェード（自動）」）。</summary>
    [ObservableProperty]
    private string songActionSummary = "";

    /// <summary>大きさ（px）の設定項目に使う画面の高さ（書き出すプロジェクトの画面の高さ）。</summary>
    public int ScreenHeight => _main.GetScreenSize().Height;

    /// <summary>ニコカラメーカー3 の AddOns の値（無ければ null）。</summary>
    private JsonObject? AddOnDefaults(string id) => _main.Nkm3Env?.AddOnSettings.GetValueOrDefault(id);

    /// <summary>曲の既定の字幕アクションの状態を作り直して知らせる（<paramref name="rebuildFields"/> なら種類が同じでも設定欄を作り直す）。</summary>
    public void RefreshSongAction(bool rebuildFields)
    {
        var auto = _main.ResolveAutoSubtitleAction(out var source);
        string autoName = N3SubtitleActionCatalog.Describe(auto);
        string sourceText = source switch
        {
            N3SubtitleActionSource.Base => "ベースのまま",
            N3SubtitleActionSource.Nkm3 => "ニコカラメーカー3 の設定",
            _ => "既定",
        };
        string label = source is N3SubtitleActionSource.Base or N3SubtitleActionSource.Nkm3
            ? $"（自動: {sourceText} → {autoName}）"
            : $"（自動: {autoName}）";
        string autoNote = source switch
        {
            N3SubtitleActionSource.Base => $"自動のときは、ベースの n3proj の歌詞行でいちばん多い字幕アクション（{autoName}）を使います。",
            N3SubtitleActionSource.Nkm3 => $"自動のときは、ニコカラメーカー3 の「すべて同じ字幕アクションにする」の設定（{autoName}）を使います（ベースの n3proj に字幕アクションが無いため）。",
            _ => "自動のときは、文字単位フェードを使います（ベースの n3proj にもニコカラメーカー3 の設定にも字幕アクションが無いため）。",
        };
        var song = _main.N3ProjSettings.SubtitleAction;
        bool isAuto = song is not { Id.Length: > 0 };
        var action = isAuto ? auto : song!.Clone();
        SongAction = new SongActionState(isAuto, action, source, label, autoNote);

        SongActionSummary = isAuto ? $"自動（{sourceText}: {autoName}）" : N3SubtitleActionCatalog.Describe(action);
        string save = _main.CanSaveSongFontSets ? "" : "\n歌詞ファイルを保存していないため、曲の既定は .tttproj に保存されません（歌詞ファイルを保存すると保存します）。";
        SongActionNote = (isAuto ? autoNote : "行ごと（ページごと）に指定の無い歌詞行に、この字幕アクションを書き出します。") +
                         "ページごとの指定は、右のページの一覧で行います。" + save;
        CanResetSongAction = !isAuto && N3SubtitleActionCatalog.IsKnown(action.Id)
            && !action.SameAs(N3SubtitleActionCatalog.CreateDefault(action.Id, AddOnDefaults(action.Id)));

        string kind = isAuto ? "\nauto" : action.Id;
        bool kindChanged = rebuildFields || kind != _songActionKind;
        _songActionKind = kind;
        SongActionChanged?.Invoke(this, kindChanged);
    }

    /// <summary>曲の既定の字幕アクションが変わった（このビュー・右パネル）: カードとページの一覧の「既定」の文字を作り直す。</summary>
    private void OnSongDefaultActionChanged()
    {
        if (!_active) return;
        RefreshSongAction(rebuildFields: false);
        RefreshPages(_main.GetLayoutPages());
        UpdateUsage();
    }

    /// <summary>
    /// 曲の既定の字幕アクションの種類を選ぶ（null = 自動）。自動で決まるものと同じ種類ならその値から、違えばニコカラメーカー3 の設定の値
    /// （AddOns。無ければ既定値）から始める。同じ種類を選び直したときは何もしない。
    /// </summary>
    public void ChangeSongActionKind(string? id)
    {
        var current = _main.N3ProjSettings.SubtitleAction;
        if (id is null)
        {
            if (current is null) return;
            if (_main.SetSongDefaultSubtitleAction(null)) SetStatus("曲の既定の字幕アクションを自動にしました");
            return;
        }
        if (current is { } c && c.Id == id) return;
        var auto = _main.ResolveAutoSubtitleAction(out _);
        var next = auto.Id == id ? auto : N3SubtitleActionCatalog.CreateDefault(id, AddOnDefaults(id));
        if (_main.SetSongDefaultSubtitleAction(next))
        {
            SetStatus($"曲の既定の字幕アクションを「{N3SubtitleActionCatalog.DisplayName(id)}」にしました（.tttproj に保存します）");
        }
    }

    /// <summary>
    /// 曲の既定の字幕アクションの設定項目を変える（自動のときは変えない）。時間（ms）は 0 以上の整数に、大きさ（px）は画面の高さ
    /// <see cref="ScreenHeight"/> での値として書く。
    /// </summary>
    public void SetSongActionValue(N3SubtitleActionField field, double number, bool flag)
    {
        if (_main.N3ProjSettings.SubtitleAction is not { Id.Length: > 0 } current) return;
        var next = current.Clone();
        switch (field.Kind)
        {
            case N3SubtitleActionFieldKind.Bool:
                next.Set(field.Key, flag);
                break;
            case N3SubtitleActionFieldKind.Pixels:
                if (!double.IsFinite(number)) return;
                next.SetPixels(field.Key, number, ScreenHeight);
                break;
            default:
                if (!double.IsFinite(number)) return;
                next.Set(field.Key, (int)Math.Round(Math.Max(0, number), MidpointRounding.AwayFromZero));
                break;
        }
        _main.SetSongDefaultSubtitleAction(next);
    }

    /// <summary>「ニコカラメーカー3 の既定値に戻す」: 選んでいる種類の値を、ニコカラメーカー3 の設定（AddOns。無ければ既定値）にする。</summary>
    public void ResetSongActionValues()
    {
        if (_main.N3ProjSettings.SubtitleAction is not { Id.Length: > 0 } current || !N3SubtitleActionCatalog.IsKnown(current.Id)) return;
        bool fromAddOn = AddOnDefaults(current.Id) is not null;
        if (_main.SetSongDefaultSubtitleAction(N3SubtitleActionCatalog.CreateDefault(current.Id, AddOnDefaults(current.Id))))
        {
            SetStatus($"曲の既定の字幕アクション「{N3SubtitleActionCatalog.DisplayName(current.Id)}」の値を、" +
                      (fromAddOn ? "ニコカラメーカー3 の設定の値に戻しました" : "ニコカラメーカー3 の既定値に戻しました"));
            RefreshSongAction(rebuildFields: true);
        }
    }
}
