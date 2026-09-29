using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App;

// デバッグ用: フォントのプレビュー（FontPreviewControl）を実機で確かめる画面
public sealed partial class MainWindow
{
    /// <summary>コマンドラインに --debug-font-preview [n3proj のパス] があれば、起動の少し後に確認画面を開く。</summary>
    private void StartFontPreviewDebugIfRequested(string[] args)
    {
        int at = Array.IndexOf(args, "--debug-font-preview");
        if (at < 0) return;
        string? path = at + 1 < args.Length ? args[at + 1] : null;
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) => await ShowFontPreviewDebugAsync(path);
        timer.Start();
    }

    /// <summary>n3proj（無ければアプリ共通のフォント設定）と確認用のフォントを切り替えながら見本を表示する。</summary>
    private async Task ShowFontPreviewDebugAsync(string? n3projPath)
    {
        var fonts = new List<N3FontSet>();
        string source = "アプリ共通のフォント設定";
        if (!string.IsNullOrEmpty(n3projPath))
        {
            try
            {
                fonts.AddRange(N3ProjFormat.ReadFontSets(n3projPath));
                source = Path.GetFileName(n3projPath);
            }
            catch (Exception ex)
            {
                source = $"{Path.GetFileName(n3projPath)} を読めません（{ex.Message}）";
            }
        }
        if (fonts.Count == 0) fonts.AddRange(ViewModel.Settings.N3FontSets.Select(f => f.Clone()));
        fonts.AddRange(CreatePreviewTestFonts());

        var preview = new FontPreviewControl { Height = 380, RubyText = "えい" };
        var fontBox = new ComboBox { Header = "フォント設定", ItemsSource = fonts.Select(f => f.Name).ToList(), Width = 340 };
        fontBox.SelectionChanged += (_, _) => preview.FontSet = fontBox.SelectedIndex >= 0 ? fonts[fontBox.SelectedIndex] : null;
        var sampleBox = new TextBox { Header = "見本の文字", Text = preview.SampleText, Width = 170 };
        sampleBox.TextChanged += (_, _) => preview.SampleText = sampleBox.Text;
        var rubyBox = new TextBox { Header = "ルビ", Text = preview.RubyText, Width = 120 };
        rubyBox.TextChanged += (_, _) => preview.RubyText = rubyBox.Text;
        var heights = new List<string> { "1080", "540", "360", "270" };
        var heightBox = new ComboBox { Header = "基準の高さ", ItemsSource = heights, SelectedIndex = 0, Width = 110 };
        heightBox.SelectionChanged += (_, _) =>
        {
            if (heightBox.SelectedItem is string h && double.TryParse(h, out double value)) preview.ReferenceHeight = value;
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        toolbar.Children.Add(fontBox);
        toolbar.Children.Add(sampleBox);
        toolbar.Children.Add(rubyBox);
        toolbar.Children.Add(heightBox);
        var panel = new StackPanel { Spacing = 12, Width = 880 };
        panel.Children.Add(new TextBlock { Text = $"読み込み元: {source}（{fonts.Count} 件。［試験］は確認用に足したもの）", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(toolbar);
        panel.Children.Add(preview);
        fontBox.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "フォントのプレビュー（デバッグ）",
            Content = panel,
            CloseButtonText = "閉じる",
        };
        dialog.Resources["ContentDialogMaxWidth"] = 960d;
        await dialog.ShowAsync();
    }

    /// <summary>n3proj の実データに無い描き方（影・縁 2・横倍率・グラデーション・画像・範囲外の値など）を確かめるフォント。</summary>
    private static IEnumerable<N3FontSet> CreatePreviewTestFonts()
    {
        static void Solid(N3FontDetail d, params string[] colors)
        {
            for (int i = 0; i < colors.Length; i++) d.Brushes[i] = new N3Brush { Color = colors[i] };
        }
        static N3Brush Stops(int type, params (double Position, string Color)[] stops) => new()
        {
            Type = type,
            Color = "808080",
            Stops = stops.Select(s => new N3GradientStop { Position = s.Position, Color = s.Color }).ToList(),
        };

        var shadow = new N3FontSet { Name = "［試験］影・縁 2・横倍率 70%" };
        var d = shadow.Detail;
        d.Faces[0] = new N3FontFace { FontName = "メイリオ", FaceName = "Bold", SizePx = 100, EdgePx = 10, UseEdge2 = true, Edge2Px = 10, XScale = 70 };
        Solid(d, "FFFFFF", "E0457B", "202020", "000000", "4DA3FF", "FFFFFF", "202020", "000000");
        d.Brushes[3].AlphaPercent = 60;
        d.Brushes[7].AlphaPercent = 60;
        d.DecorKind = 1;
        d.DecorSizePx = 12;
        yield return shadow;

        var gradient = new N3FontSet { Name = "［試験］グラデーション・ミルフィーユ・ブラー濃さ 2" };
        d = gradient.Detail;
        d.Faces[0] = new N3FontFace { FontName = "HGP創英角ﾎﾟｯﾌﾟ体", FaceName = "ﾍﾋﾞｰ", SizePx = 100, EdgePx = 12 };
        d.Faces[3] = new N3FontFace { FontName = "游ゴシック", FaceName = "Bold", SizePx = 45, EdgePx = 8 };
        Solid(d, "FFFFFF", "000000", "FFFFFF", "FFFFFF", "FFFFFF", "FFFFFF", "000000", "000000");
        d.Brushes[0] = Stops(N3Brush.TypeGradient, (0, "FFF200"), (0.5, "FF8C00"), (1, "E00050"));
        d.Brushes[1] = Stops(N3Brush.TypeMilleFeuille, (0, "E00000"), (0.34, "00A000"), (0.67, "0050FF"), (1, "808080"));
        d.Brushes[4] = Stops(N3Brush.TypeMilleFeuille, (0, "CCB12E"), (0.5, "66C5EC"), (1, "808080"));
        d.Brushes[5] = Stops(N3Brush.TypeGradient);
        d.Brushes[7] = new N3Brush { Color = "66C5EC" };
        d.DecorKind = 2;
        d.DecorSizePx = 16;
        d.BlurLevel = 2;
        yield return gradient;

        var image = new N3FontSet { Name = "［試験］画像（読める画像・無い画像）" };
        d = image.Detail;
        d.Faces[0] = new N3FontFace { FontName = "メイリオ", FaceName = "Bold", SizePx = 110, EdgePx = 8 };
        Solid(d, "FFFFFF", "000000", "FFFFFF", "000000", "4DA3FF", "FFFFFF", "000000", "000000");
        d.Brushes[0] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), BitmapScale = 25 };
        d.Brushes[4] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = @"C:\存在しないフォルダ\画像.png" };
        d.DecorKind = 0;
        yield return image;

        var fallback = new N3FontSet { Name = "［試験］既定のフォント（空）・未指定の色" };
        d = fallback.Detail;
        d.Faces[0] = new N3FontFace();
        d.Brushes = Enumerable.Range(0, N3FontDetail.BrushCount).Select(_ => new N3Brush()).ToArray();
        d.DecorKind = 0;
        yield return fallback;

        var broken = new N3FontSet { Name = "［試験］無いフォント・範囲外の値" };
        d = broken.Detail;
        d.Faces[0] = new N3FontFace { FontName = "存在しないフォント名", FaceName = "ｴｸｽﾄﾗﾎﾞｰﾙﾄﾞ", SizePx = double.NaN, EdgePx = -5, UseEdge2 = true, Edge2Px = 6, XScale = -20 };
        Solid(d, "ZZZZZZ", "000000", "FFFFFF", "000000", "4DA3FF", "FFFFFF", "000000", "000000");
        d.Brushes[1].Type = 9;
        d.Brushes[4] = new N3Brush { Type = N3Brush.TypeGradient, Stops = new() { new N3GradientStop { Position = double.NaN, Color = "FF0000" }, new N3GradientStop { Position = 3, Color = "00FF00", AlphaPercent = 250 } } };
        d.DecorKind = 2;
        d.DecorSizePx = double.PositiveInfinity;
        d.BlurLevel = 7;
        yield return broken;
    }
}
