using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 絵文字挿入ビューの行の編集画面（行のダブルクリック・簡易行エディタの「行の編集...」）。歌詞（タイムタグ付き）と表示開始・終了をまとめて直す。
/// 表示開始・終了の欄の文字は手で指定した値だけで、薄字は今の表示時刻（行リストの行設定の欄と同じ）。適用で入力を確かめ、結果を <see cref="RawText"/> などに入れる。
/// </summary>
public sealed partial class LineEditDialog : ContentDialog
{
    /// <param name="lineNumber">行番号（1 始まり）。</param>
    /// <param name="raw">行の生テキスト。</param>
    /// <param name="beginText">手で指定した表示開始（無ければ空）。</param>
    /// <param name="endText">手で指定した表示終了（無ければ空）。</param>
    /// <param name="beginPlaceholder">今の表示開始（薄字）。</param>
    /// <param name="endPlaceholder">今の表示終了（薄字）。</param>
    /// <param name="sung">歌い出し・歌い終わりなどの説明。</param>
    /// <param name="timesEnabled">表示時刻を直せるか（空行は直せない）。</param>
    public LineEditDialog(int lineNumber, string raw, string beginText, string endText, string beginPlaceholder, string endPlaceholder, string sung, bool timesEnabled)
    {
        InitializeComponent();
        Title = $"{lineNumber} 行目の編集";
        RawBox.Text = raw;
        BeginBox.Text = beginText;
        EndBox.Text = endText;
        BeginBox.PlaceholderText = beginPlaceholder;
        EndBox.PlaceholderText = endPlaceholder;
        SungText.Text = sung;
        BeginBox.IsEnabled = EndBox.IsEnabled = ResetButton.IsEnabled = timesEnabled;
        PrimaryButtonClick += OnPrimaryClick;
        Opened += (_, _) => RawBox.Focus(FocusState.Programmatic);
    }

    /// <summary>直した歌詞（生テキスト）。</summary>
    public string RawText { get; private set; } = "";

    /// <summary>表示開始の欄の値（空欄なら null）。</summary>
    public int? BeginCs { get; private set; }

    /// <summary>表示終了の欄の値（空欄なら null）。</summary>
    public int? EndCs { get; private set; }

    /// <summary>「自動に戻す」を押したか（読み込んだ値・自動調整の値も外す）。</summary>
    public bool ResetToAuto { get; private set; }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        BeginBox.Text = "";
        EndBox.Text = "";
        ResetToAuto = true;
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 欄に入ったときに入れた今の表示時刻のままなら空欄（自動のまま）
        if (!TryParse(PlaceholderPrefill.EffectiveText(BeginBox), out int? begin) || !TryParse(PlaceholderPrefill.EffectiveText(EndBox), out int? end))
        {
            ErrorText.Text = "表示開始・終了は mm:ss:cc 形式で入力してください（空欄なら自動）";
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }
        RawText = RawBox.Text;
        BeginCs = begin;
        EndCs = end;
    }

    private static bool TryParse(string text, out int? cs)
    {
        cs = null;
        string t = text.Trim();
        if (t.Length == 0) return true;
        if (!TimeTag.TryParse(t, out int v)) return false;
        cs = v;
        return true;
    }
}
