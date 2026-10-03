using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>行リストの 1 行分。</summary>
public partial class LineViewModel : ObservableObject
{
    public LineViewModel(LyricsLine model, int index)
    {
        Model = model;
        Index = index;
    }

    public LyricsLine Model { get; private set; }

    [ObservableProperty]
    private int index;

    /// <summary>行リストで選択中かどうか（チェックボックス表示用。ListView の選択と同期）。</summary>
    [ObservableProperty]
    private bool isSelected;

    public string IndexText => (Index + 1).ToString();

    /// <summary>行頭タイムタグ表示。</summary>
    public string TimeText
    {
        get
        {
            int? t = Model.GetFirstTimeCs();
            return t is int cs ? TimeTag.Format(cs).Trim('[', ']') : "";
        }
    }

    /// <summary>行末（終了）タイムタグ表示。</summary>
    public string EndTimeText
    {
        get
        {
            int? t = Model.GetLastTimeCs();
            return t is int cs ? TimeTag.Format(cs).Trim('[', ']') : "";
        }
    }

    // 横幅の判定（横幅の欄のツールチップに画面の横幅・左右余白を出す）
    private LineWidthResult? _widthResult;

    /// <summary>横幅の欄のツールチップ。</summary>
    public string WidthToolTip =>
        "横幅 px（字幕のプレビューと同じ並べ方で、この行に当たるフォント設定で測った幅）と、" +
        "ページのレイアウト設定の左右余白を除いた幅に対する使用率。90% 超はオレンジで予告"
        + (_widthResult is { } r ? $"\n画面 {r.ScreenWidthPx}px・左右余白 {r.SideMarginPx:F0}px（余白を除いた幅 {r.UsableWidthPx:F0}px）" : "");

    /// <summary>この行に当たるフォント設定（n3proj の書き出しと同じ決め方。チェックのたびに更新する）。</summary>
    [ObservableProperty]
    private LineFontDisplay appliedFont = LineFontDisplay.None;

    /// <summary>この行のページのレイアウト設定名（手動指定なら先頭に ✎。チェックのたびに更新する）。表示時刻が決まらない行は空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLayoutText))]
    private string layoutText = "";

    /// <summary>レイアウトの決まり方の説明（ツールチップ）。</summary>
    [ObservableProperty]
    private string layoutToolTip = "";

    public bool HasLayoutText => LayoutText.Length > 0;

    /// <summary>歌詞を字幕の見た目で描く材料（null なら文字のまま表示する）。チェックのたびに更新する。</summary>
    public Services.Subtitles.LineRenderSource? RenderSource { get; private set; }

    /// <summary>歌詞を字幕の見た目で描くか。</summary>
    public bool ShowStyledText => RenderSource is not null;

    /// <summary>歌詞を文字のまま表示するか。</summary>
    public bool ShowPlainText => RenderSource is null;

    /// <summary>字幕の見た目で描く材料を設定する（描く内容が同じなら何もしない）。</summary>
    public void SetRenderSource(Services.Subtitles.LineRenderSource? source)
    {
        if (source is null ? RenderSource is null : RenderSource is not null && RenderSource.Key == source.Key && ReferenceEquals(RenderSource.Line, source.Line)) return;
        bool styledChanged = (source is null) != (RenderSource is null);
        RenderSource = source;
        OnPropertyChanged(nameof(RenderSource));
        if (styledChanged)
        {
            OnPropertyChanged(nameof(ShowStyledText));
            OnPropertyChanged(nameof(ShowPlainText));
        }
    }

    /// <summary>行リストで選んでいる文字（<see cref="LyricsLine.Chars"/> の添字の範囲、両端を含む）。無ければ null。</summary>
    [ObservableProperty]
    private (int Start, int End)? charSelection;

    /// <summary>表示テキスト（空行は視認用の記号。タグだけが残った行は注意書きを出す）。</summary>
    public string DisplayText
    {
        get
        {
            if (Model.IsEmpty) return "── ページ区切り ──";
            string text = Model.GetDisplayText();
            return text.Length == 0 ? "（タグのみの行 ─ ページ区切りにはなりません）" : text;
        }
    }

    public bool Exported
    {
        get => Model.Exported;
        set
        {
            if (Model.Exported == value) return;
            Model.Exported = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(ExportedMark));
        }
    }

    public string ExportedMark => Model.Exported ? "✓" : "";

    /// <summary>ニコカラメーカー用の手動指定（表示時刻・フォント設定）がある行の印。</summary>
    public string OverrideMark => Model.HasManualN3Overrides ? "✎" : ""; // 手で指定したものだけ（読み込んだ表示時刻・自動調整の値は付けない）

    public void RaiseOverrideMark() => OnPropertyChanged(nameof(OverrideMark));

    public double Opacity => Model.Exported ? 0.45 : 1.0;

    /// <summary>テキスト編集モード形式の生テキスト（行エディタ用）。</summary>
    public string RawText => TextEditModeFormat.WriteLyricLine(Model);

    // ------------------------------------------------------------ 検証結果

    [ObservableProperty]
    private string widthText = "";

    /// <summary>横幅チェックの結果記号（"" / ⚠ / ⛔）。</summary>
    [ObservableProperty]
    private string widthGlyph = "";

    /// <summary>同時歌唱などの重なり情報（正常ケースの目印）。</summary>
    [ObservableProperty]
    private string overlapGlyph = "";

    /// <summary>横幅チェックの重要度（挿入ビューの行情報欄の色分け用）。</summary>
    public IssueSeverity? WidthSeverity { get; private set; }

    /// <summary>有効幅（マージン除き）に対する使用率 %（挿入ビューの行情報欄の色分け用）。</summary>
    public double WidthUsagePercent { get; private set; }

    public void SetWidthResult(LineWidthResult? result)
    {
        if (result is null || Model.IsEmpty) result = null;
        if (!Equals(_widthResult, result))
        {
            _widthResult = result;
            OnPropertyChanged(nameof(WidthToolTip));
        }
        if (result is null)
        {
            WidthText = "";
            WidthGlyph = "";
            WidthSeverity = null;
            WidthUsagePercent = 0;
            OnPropertyChanged(nameof(WidthBrush));
            return;
        }
        WidthText = $"{result.WidthPx:F0}px {result.UsagePercent:F0}%";
        WidthSeverity = result.Severity;
        WidthUsagePercent = result.UsagePercent;
        WidthGlyph = result.Severity switch
        {
            IssueSeverity.Error => "⛔",
            IssueSeverity.Warning => "⚠",
            _ => "",
        };
        OnPropertyChanged(nameof(WidthBrush));
    }

    /// <summary>
    /// 横幅セルの文字色。マージン不足・はみ出しは重要度の色、
    /// それ以外でも使用率 90% 超は警告色で予告（挿入ビューの行情報欄と同じ規則）。
    /// </summary>
    public Microsoft.UI.Xaml.Media.Brush WidthBrush =>
        WidthSeverity switch
        {
            IssueSeverity.Error => ErrorTimeBrush,
            IssueSeverity.Warning => WarningTimeBrush,
            _ => WidthUsagePercent > 90
                ? WarningTimeBrush
                : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

    public void SetOverlapInfo(bool overlapped) => OverlapGlyph = overlapped ? "♪" : "";

    // ------------------------------------------------ 行の強調表示（再生追従・チェック結果）

    private static readonly Microsoft.UI.Xaml.Media.Brush TransparentBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private static readonly Microsoft.UI.Xaml.Media.Brush CurrentLineBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x00, 0x99, 0xFF));

    private static readonly Microsoft.UI.Xaml.Media.Brush ErrorRowBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x2C, 0xE8, 0x11, 0x23));

    private static readonly Microsoft.UI.Xaml.Media.Brush WarningRowBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 0xFF, 0xB9, 0x00));

    internal static readonly Microsoft.UI.Xaml.Media.Brush ErrorTimeBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x11, 0x23));

    internal static readonly Microsoft.UI.Xaml.Media.Brush WarningTimeBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0x7A, 0x00));

    private bool _isCurrent;
    private IssueSeverity? _rowSeverity;
    private IssueSeverity? _startTimeSeverity;
    private IssueSeverity? _endTimeSeverity;

    /// <summary>ページ衝突・表示時刻のチェックの重要度（開始時間側。挿入ビューの行情報欄の色分け用）。</summary>
    public IssueSeverity? StartTimeSeverity => _startTimeSeverity;

    /// <summary>ページ衝突・表示時刻のチェックの重要度（終了時間側。挿入ビューの行情報欄の色分け用）。</summary>
    public IssueSeverity? EndTimeSeverity => _endTimeSeverity;

    /// <summary>再生位置がこの行にあるとき true。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            OnPropertyChanged(nameof(RowBrush));
        }
    }

    /// <summary>行の背景色（再生中 > エラー > 警告 > 透明）。</summary>
    public Microsoft.UI.Xaml.Media.Brush RowBrush =>
        _isCurrent ? CurrentLineBrush
        : _rowSeverity switch
        {
            IssueSeverity.Error => ErrorRowBrush,
            IssueSeverity.Warning => WarningRowBrush,
            _ => TransparentBrush,
        };

    /// <summary>開始時間セルの文字色（ページ衝突の対象なら強調）。</summary>
    public Microsoft.UI.Xaml.Media.Brush? StartTimeBrush => _startTimeSeverity switch
    {
        IssueSeverity.Error => ErrorTimeBrush,
        IssueSeverity.Warning => WarningTimeBrush,
        _ => (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"],
    };

    /// <summary>終了時間セルの文字色（ページ衝突の対象なら強調）。</summary>
    public Microsoft.UI.Xaml.Media.Brush? EndTimeBrush => _endTimeSeverity switch
    {
        IssueSeverity.Error => ErrorTimeBrush,
        IssueSeverity.Warning => WarningTimeBrush,
        _ => (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    /// <summary>チェック結果の強調をすべて解除する（再チェックの前に呼ぶ）。</summary>
    public void ResetIssueMarks()
    {
        _rowSeverity = null;
        _startTimeSeverity = null;
        _endTimeSeverity = null;
        RaiseIssueMarks();
    }

    /// <summary>行全体の背景色に反映する重要度を設定（高い方を維持）。</summary>
    public void SetRowIssue(IssueSeverity severity)
    {
        if (_rowSeverity is null || severity > _rowSeverity)
        {
            _rowSeverity = severity;
            OnPropertyChanged(nameof(RowBrush));
        }
    }

    /// <summary>開始時間セルを強調（ページ衝突・表示時刻のチェックで「次行の表示開始」側）。</summary>
    public void MarkStartTimeIssue(IssueSeverity severity)
    {
        if (_startTimeSeverity is null || severity > _startTimeSeverity)
        {
            _startTimeSeverity = severity;
            OnPropertyChanged(nameof(StartTimeBrush));
        }
    }

    /// <summary>終了時間セルを強調（ページ衝突・表示時刻のチェックで「前行の表示終了」側）。</summary>
    public void MarkEndTimeIssue(IssueSeverity severity)
    {
        if (_endTimeSeverity is null || severity > _endTimeSeverity)
        {
            _endTimeSeverity = severity;
            OnPropertyChanged(nameof(EndTimeBrush));
        }
    }

    private void RaiseIssueMarks()
    {
        OnPropertyChanged(nameof(RowBrush));
        OnPropertyChanged(nameof(StartTimeBrush));
        OnPropertyChanged(nameof(EndTimeBrush));
    }

    /// <summary>モデル差し替え（行エディタからの適用時）。</summary>
    public void ReplaceModel(LyricsLine newModel)
    {
        newModel.Exported = Model.Exported;
        newModel.ShowBeginCs = Model.ShowBeginCs;
        newModel.ShowEndCs = Model.ShowEndCs;
        newModel.ShowBeginOrigin = Model.ShowBeginOrigin;
        newModel.ShowEndOrigin = Model.ShowEndOrigin;
        newModel.FontSetName = Model.FontSetName;
        newModel.LayoutName = Model.LayoutName;
        Model = newModel;
        RaiseAllChanged();
    }

    public void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(Exported));
        OnPropertyChanged(nameof(ExportedMark));
        OnPropertyChanged(nameof(OverrideMark));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(IndexText));
    }
}
