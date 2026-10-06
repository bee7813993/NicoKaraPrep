using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 表示時刻の自動調整（ニコカラメーカー3 の「自動で表示開始時刻・表示終了時刻を設定」と同じ計算）のパラメーターと実行のポップアップ。
/// 行リストの行設定の「自動調整...」・エクスポート(X) から開く。パラメーターはアプリ共通で、変えるとすぐ保存する（閉じるボタンだけ）。
/// 行リスト・行設定・チェックの作り直しはメイン画面が受け持つ（<see cref="ShowTimeSettingsChanged"/>・<see cref="AutoShowTimeRequested"/>）。
/// </summary>
public sealed partial class ShowTimeDialog : ContentDialog
{
    private readonly MainViewModel _vm;

    /// <summary>欄に値を入れている最中か（そのあいだの欄の変化は設定の変更として扱わない）。</summary>
    private bool _loading;

    public ShowTimeDialog(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Refresh();
    }

    /// <summary>表示時刻のパラメーターを変えた（設定は保存済み。メイン画面はプレビュー・行設定・チェックを作り直す）。</summary>
    public event EventHandler? ShowTimeSettingsChanged;

    /// <summary>「自動調整を実行」を押した。</summary>
    public event EventHandler? AutoShowTimeRequested;

    /// <summary>欄を設定の値に合わせ、行数の説明を作り直す（MCP でパラメーターを変えたときも呼ぶ）。</summary>
    public void Refresh()
    {
        var s = _vm.Settings;
        _loading = true;
        try
        {
            StLeadBox.Value = s.DisplayLeadSeconds;
            StTailBox.Value = s.DisplayTailSeconds;
            StIntervalBox.Value = s.N3IntervalSeconds;
            StProtectBox.Value = s.N3ProtectSeconds;
            StOverlapBox.Value = s.N3OverlapSeconds;
            StTopLongCheck.IsChecked = s.N3TopLong;
            StEmojiYieldCheck.IsChecked = s.N3EmojiLeadYield;
            StLayoutAwareCheck.IsChecked = s.N3LayoutAwareRows;
            if (_vm.Nkm3Env is { PreTimeMs: not null } env)
            {
                StImportNkm3Button.Visibility = Visibility.Visible;
                StImportNkm3Text.Text = $"ニコカラメーカーの設定値を取り込む（ワイプ前 {env.PreTimeMs / 1000.0:0.##} 秒・ワイプ後 {env.PostTimeMs / 1000.0:0.##} 秒・表示間隔 {env.IntervalMs / 1000.0:0.##} 秒）";
            }
        }
        finally
        {
            _loading = false;
        }
        RefreshSummary();
    }

    /// <summary>表示中のタブの、表示時刻の出どころごとの行数と、実行し直すと変わる行の数を出す。</summary>
    public void RefreshSummary()
    {
        var (c, outdated) = _vm.ShowTimeSummary();
        var parts = new List<string>();
        if (c.Manual > 0) parts.Add($"手で直した {c.Manual} 行");
        if (c.Loaded > 0) parts.Add($"読み込んだ {c.Loaded} 行");
        if (c.Auto > 0) parts.Add($"自動調整の {c.Auto} 行");
        if (c.Live > 0) parts.Add($"未設定（自動で計算）{c.Live} 行");
        string text = parts.Count == 0 ? "表示時刻を決められる行がありません" : "表示中のタブ: " + string.Join("・", parts);
        if (outdated > 0) text += $"\n今のパラメーターで実行し直すと {outdated} 行が変わります";
        ShowTimeSummaryText.Text = text;
    }

    private void OnShowTimeNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (double.IsNaN(args.NewValue))
        {
            Refresh(); // 空にした欄は元の値に戻す
            return;
        }
        var s = _vm.Settings;
        double v = Math.Max(0, args.NewValue);
        if (ReferenceEquals(sender, StLeadBox)) s.DisplayLeadSeconds = v;
        else if (ReferenceEquals(sender, StTailBox)) s.DisplayTailSeconds = v;
        else if (ReferenceEquals(sender, StIntervalBox)) s.N3IntervalSeconds = v;
        else if (ReferenceEquals(sender, StProtectBox)) s.N3ProtectSeconds = v;
        else if (ReferenceEquals(sender, StOverlapBox)) s.N3OverlapSeconds = v;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnShowTimeCheckClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _vm.Settings;
        if (ReferenceEquals(sender, StTopLongCheck)) s.N3TopLong = StTopLongCheck.IsChecked == true;
        else if (ReferenceEquals(sender, StEmojiYieldCheck)) s.N3EmojiLeadYield = StEmojiYieldCheck.IsChecked == true;
        else if (ReferenceEquals(sender, StLayoutAwareCheck)) s.N3LayoutAwareRows = StLayoutAwareCheck.IsChecked == true;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStImportNkm3Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Nkm3Env is not { } env) return;
        var s = _vm.Settings;
        if (env.PreTimeMs is int pre) s.DisplayLeadSeconds = pre / 1000.0;
        if (env.PostTimeMs is int post) s.DisplayTailSeconds = post / 1000.0;
        if (env.IntervalMs is int interval) s.N3IntervalSeconds = interval / 1000.0;
        s.Save();
        Refresh();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRunAutoShowTimeClick(object sender, RoutedEventArgs e) => AutoShowTimeRequested?.Invoke(this, EventArgs.Empty);
}
