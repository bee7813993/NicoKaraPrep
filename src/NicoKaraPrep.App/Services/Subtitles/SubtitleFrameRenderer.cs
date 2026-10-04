using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 字幕のプレビューの 1 コマを、画面に出さずに画像（PNG）にする（MCP の render_preview）。
/// 描き方は動画の上のプレビュー（<see cref="Views.SubtitlePreviewView"/>）と同じで、動画は含めず暗い背景に描く。UI スレッドから呼ぶ。
/// </summary>
internal static class SubtitleFrameRenderer
{
    /// <summary>
    /// 指定の時刻（タグの時刻の基準の ms）のコマを、幅 <paramref name="width"/> px（高さは字幕の画面の縦横比）の PNG にする。
    /// 絵文字の画像などを読み込み中なら、読み終わるのを少し待って描き直す。
    /// </summary>
    public static async Task<byte[]> RenderPngAsync(SubtitlePreviewModel model, double timeMs, int width)
    {
        width = Math.Clamp(width, 160, 3840);
        int height = Math.Max(90, (int)Math.Round((double)width * model.ScreenHeight / model.ScreenWidth));
        var device = SubtitleGlyphCache.Device;
        using var target = new CanvasRenderTarget(device, width, height, 96);
        for (int attempt = 0; ; attempt++)
        {
            bool loading;
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Views.SubtitlePreviewView.NoVideoColor);
                loading = Views.SubtitlePreviewView.DrawFrame(ds, model, timeMs, width, height, hasVideo: false, showSubtitles: true);
            }
            if (!loading || attempt >= 6) break;
            await WaitBitmapLoadedAsync(TimeSpan.FromMilliseconds(500));
        }
        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>画像を 1 つ読み終えるか、時間が過ぎるまで待つ。</summary>
    private static async Task WaitBitmapLoadedAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object? sender, EventArgs e) => tcs.TrySetResult(true);
        SubtitleBitmapCache.Loaded += OnLoaded;
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        }
        finally
        {
            SubtitleBitmapCache.Loaded -= OnLoaded;
        }
    }
}
