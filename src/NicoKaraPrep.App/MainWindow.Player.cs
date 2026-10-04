using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;

namespace NicoKaraPrep.App;

/// <summary>
/// メディア再生パネルの再生の操作（動画の下の行。再生・一時停止、再生位置、長さ、音量、字幕プレビューの切り替え）と、
/// 動画の上の字幕のプレビューの時刻の更新（再生中は画面の描き換えごと、止めているあいだは位置を動かしたとき）。
/// </summary>
public sealed partial class MainWindow
{
    private MediaPlayer? _observedPlayer;
    private bool _frameHooked;
    private bool _updatingSeekSlider;
    private int _frameCount;

    /// <summary>起動時に、プレビューの設定と材料をつなぐ。</summary>
    private void InitializePlayerBar()
    {
        SubtitlePreviewToggle.IsChecked = ViewModel.Settings.PlayerSubtitlePreview;
        SubtitlePreview.ShowSubtitles = ViewModel.Settings.PlayerSubtitlePreview;
        SubtitlePreview.Model = ViewModel.PreviewModel;
        ViewModel.PreviewModelChanged += (_, _) => SubtitlePreview.Model = ViewModel.PreviewModel;
    }

    /// <summary>メディアを開いたあと、再生の状態の知らせを受け取る（MediaPlayer は最初に開いたときに作られる）。</summary>
    private void ObservePlayer()
    {
        var player = Player.MediaPlayer;
        if (player is null || ReferenceEquals(player, _observedPlayer)) return;
        if (_observedPlayer is not null)
        {
            _observedPlayer.MediaOpened -= OnMediaOpened;
            _observedPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        }
        _observedPlayer = player;
        player.MediaOpened += OnMediaOpened;
        player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        player.Volume = PlayerVolumeSlider.Value / 100.0;
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var session = sender.PlaybackSession;
            SubtitlePreview.HasVideo = session.NaturalVideoHeight > 0;
            double seconds = session.NaturalDuration.TotalSeconds;
            ViewModel.MediaDurationCs = seconds > 0 ? (int)Math.Round(seconds * 100) : null; // 後奏の定型文を曲の終わりまで表示する
            _updatingSeekSlider = true;
            try
            {
                PlayerSeekSlider.Maximum = Math.Max(1, seconds);
                PlayerSeekSlider.Value = 0;
            }
            finally
            {
                _updatingSeekSlider = false;
            }
            PlayerDurationText.Text = FormatMediaTime(seconds, withFraction: false);
            UpdatePlayerTime();
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            bool playing = sender.PlaybackState == MediaPlaybackState.Playing;
            PlayPauseIcon.Glyph = playing ? "" : "";
            HookFrames(playing);
            UpdatePlayerTime();
        });
    }

    /// <summary>再生中は画面の描き換えごとにプレビューの時刻を進める。</summary>
    private void HookFrames(bool on)
    {
        if (on == _frameHooked) return;
        _frameHooked = on;
        if (on) CompositionTarget.Rendering += OnPlayerFrame;
        else CompositionTarget.Rendering -= OnPlayerFrame;
    }

    private void OnPlayerFrame(object? sender, object e)
    {
        if (PlayerWithSource is not { } player)
        {
            HookFrames(false);
            return;
        }
        var position = player.PlaybackSession.Position.TotalSeconds;
        SubtitlePreview.TimeMs = ViewModel.MediaSecondsToTagCs(position) * 10.0;
        // 位置の表示とスライダーは毎回でなくてよい（6 回に 1 回）
        if (++_frameCount % 6 == 0) UpdatePlayerTime();
    }

    /// <summary>再生位置の表示・スライダー・プレビューの時刻を今の位置にする。</summary>
    private void UpdatePlayerTime()
    {
        if (PlayerWithSource is not { } player) return;
        double seconds = player.PlaybackSession.Position.TotalSeconds;
        PlayerTimeText.Text = FormatMediaTime(seconds, withFraction: true);
        _updatingSeekSlider = true;
        try
        {
            if (seconds <= PlayerSeekSlider.Maximum) PlayerSeekSlider.Value = seconds;
        }
        finally
        {
            _updatingSeekSlider = false;
        }
        SubtitlePreview.TimeMs = ViewModel.MediaSecondsToTagCs(seconds) * 10.0;
    }

    private void OnPlayerSeekChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeekSlider || PlayerWithSource is not { } player) return;
        player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, e.NewValue));
        PlayerTimeText.Text = FormatMediaTime(e.NewValue, withFraction: true);
        SubtitlePreview.TimeMs = ViewModel.MediaSecondsToTagCs(e.NewValue) * 10.0;
    }

    private void OnPlayerVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (Player?.MediaPlayer is { } player) player.Volume = Math.Clamp(e.NewValue / 100.0, 0, 1);
    }

    /// <summary>字幕プレビュー（動画の上に字幕の見え方を重ねる）を入れる・切る。</summary>
    private void OnSubtitlePreviewToggleClick(object sender, RoutedEventArgs e)
    {
        bool on = SubtitlePreviewToggle.IsChecked == true;
        ViewModel.Settings.PlayerSubtitlePreview = on;
        ViewModel.Settings.Save();
        SubtitlePreview.ShowSubtitles = on;
        TryRun(ViewModel.UpdateLineFonts); // 材料を作る（切ったときは捨てる）
        UpdatePlayerTime();
    }

    private static string FormatMediaTime(double seconds, bool withFraction)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        int minutes = (int)(seconds / 60);
        double rest = seconds - minutes * 60;
        return withFraction ? $"{minutes}:{rest:00.00}" : $"{minutes}:{(int)rest:00}";
    }
}
