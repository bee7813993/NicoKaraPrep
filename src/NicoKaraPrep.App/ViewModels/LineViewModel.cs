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

    // 幅チェックに適用中のフォント（全行共通。n3proj 取り込み結果の確認用）
    [ObservableProperty]
    private string fontText = "";

    [ObservableProperty]
    private string fontSizeText = "";

    public void SetFontInfo(string family, double sizePx)
    {
        if (Model.IsEmpty)
        {
            FontText = "";
            FontSizeText = "";
            return;
        }
        FontText = family;
        FontSizeText = $"{sizePx:F0}";
    }

    /// <summary>表示テキスト（空行は視認用の記号）。</summary>
    public string DisplayText => Model.IsEmpty ? "── ページ区切り ──" : Model.GetDisplayText();

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

    public void SetWidthResult(LineWidthResult? result)
    {
        if (result is null || Model.IsEmpty)
        {
            WidthText = "";
            WidthGlyph = "";
            return;
        }
        WidthText = $"{result.WidthPx:F0}px {result.UsagePercent:F0}%";
        WidthGlyph = result.Severity switch
        {
            IssueSeverity.Error => "⛔",
            IssueSeverity.Warning => "⚠",
            _ => "",
        };
    }

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

    private static readonly Microsoft.UI.Xaml.Media.Brush ErrorTimeBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x11, 0x23));

    private static readonly Microsoft.UI.Xaml.Media.Brush WarningTimeBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0x7A, 0x00));

    private bool _isCurrent;
    private IssueSeverity? _rowSeverity;
    private IssueSeverity? _startTimeSeverity;
    private IssueSeverity? _endTimeSeverity;

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

    /// <summary>開始時間セルを強調（ページ衝突で「次行の表示開始」側）。</summary>
    public void MarkStartTimeIssue(IssueSeverity severity)
    {
        if (_startTimeSeverity is null || severity > _startTimeSeverity)
        {
            _startTimeSeverity = severity;
            OnPropertyChanged(nameof(StartTimeBrush));
        }
    }

    /// <summary>終了時間セルを強調（ページ衝突で「前行の表示終了」側）。</summary>
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
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(IndexText));
    }
}
