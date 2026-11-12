using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NicoKaraPrep.App;

/// <summary>
/// 0 秒より前（マイナス）の時刻の書き出しの前の確認（n3proj の書き出し・行のエクスポート）。保存（上書き保存など）は確認せず、
/// 0 秒にして書いたことをステータスバーで知らせる（<see cref="ViewModels.MainViewModel.ClampedNote"/>）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// <paramref name="count"/> 個の 0 秒より前の時刻があれば、0 秒にして書き出すかを確かめる。続けるなら true（無ければ確かめずに true）。
    /// </summary>
    private async Task<bool> ConfirmNegativeTimesAsync(int count, string what)
    {
        if (count == 0) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "0 秒より前の時刻があります",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Text = $"{what}に、0 秒より前（マイナス）の時刻が {count} 個あります。\n\n" +
                       "書き出すファイルでは 0 秒にします。アプリの中ではマイナスのまま持つので、あとで字幕全体の時刻を遅らせると、この値から計算します。",
            },
            PrimaryButtonText = "0 秒にして書き出す",
            CloseButtonText = "やめる",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
