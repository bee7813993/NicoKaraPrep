using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NicoKaraPrep.App.Views;

/// <summary>絵文字リスト編集ダイアログの 1 行分。</summary>
public partial class EmojiSlotRow : ObservableObject
{
    /// <summary>キー表示（上から 20 行は 1–0 / Q–P、以降は −）。</summary>
    [ObservableProperty]
    private string keyLabel = "−";

    [ObservableProperty]
    private string replaceChar = "";

    [ObservableProperty]
    private string imageBefore = "";

    [ObservableProperty]
    private string imageAfter = "";

    [ObservableProperty]
    private string options = "";

    [ObservableProperty]
    private bool isSongOverride;

    public bool IsEmpty => string.IsNullOrWhiteSpace(ReplaceChar);

    public EmojiEntry ToEntry(int? slot) => new()
    {
        ReplaceChar = ReplaceChar.Trim(),
        ImageBefore = ImageBefore.Trim(),
        ImageAfter = string.IsNullOrWhiteSpace(ImageAfter) ? null : ImageAfter.Trim(),
        Options = string.IsNullOrWhiteSpace(Options) ? null : Options.Trim(),
        Slot = slot,
    };

    public static EmojiSlotRow From(EmojiEntry e, bool songOverride) => new()
    {
        ReplaceChar = e.ReplaceChar,
        ImageBefore = e.ImageBefore,
        ImageAfter = e.ImageAfter ?? "",
        Options = e.Options ?? "",
        IsSongOverride = songOverride,
    };

    public EmojiSlotRow Clone() => new()
    {
        ReplaceChar = ReplaceChar,
        ImageBefore = ImageBefore,
        ImageAfter = ImageAfter,
        Options = Options,
        IsSongOverride = IsSongOverride,
    };
}

public sealed partial class EmojiListDialog : ContentDialog
{
    private readonly AppSettings _settings;
    private readonly LyricsDocument _document;
    private EmojiSlotRow? _previewRow;

    public ObservableCollection<EmojiSlotRow> Rows { get; } = new();

    /// <summary>OK で曲側のリストが変更されたかどうか。</summary>
    public bool SongListChanged { get; private set; }

    public EmojiListDialog(AppSettings settings, LyricsDocument document)
    {
        _settings = settings;
        _document = document;

        // ContentDialog の既定最大幅(約548px)ではリストが右側で見切れるため広げる
        Resources["ContentDialogMaxWidth"] = 1150d;
        Resources["ContentDialogMaxHeight"] = 950d;

        // 実効リスト全体を行に展開する:
        // スロット 1–20（曲が優先）→ スロット外の曲エントリ → スロット外のグローバル
        for (int slot = 1; slot <= 20; slot++)
        {
            var song = document.EmojiEntries.FirstOrDefault(e => e.Slot == slot);
            var global = settings.GlobalEmojiList.FirstOrDefault(e => e.Slot == slot);
            if (song is not null) Rows.Add(EmojiSlotRow.From(song, songOverride: true));
            else if (global is not null) Rows.Add(EmojiSlotRow.From(global, songOverride: false));
        }
        foreach (var e in document.EmojiEntries.Where(e => e.Slot is null))
        {
            Rows.Add(EmojiSlotRow.From(e, songOverride: true));
        }
        foreach (var e in settings.GlobalEmojiList.Where(e => e.Slot is null))
        {
            Rows.Add(EmojiSlotRow.From(e, songOverride: false));
        }
        if (Rows.Count == 0) Rows.Add(new EmojiSlotRow());

        InitializeComponent();
        UpdateKeyLabels();
        PrimaryButtonClick += (_, _) => Apply();
    }

    /// <summary>行の並び順に応じてキー表示を振り直す（上から 20 行にキー）。</summary>
    private void UpdateKeyLabels()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].KeyLabel = i < 20 ? EmojiSlotViewModel.KeyLabels[i] : "−";
        }
    }

    /// <summary>行の内容から曲リスト・グローバルリストを丸ごと再構築する。</summary>
    private void Apply()
    {
        _document.EmojiEntries.Clear();
        _settings.GlobalEmojiList.Clear();

        int position = 0;
        foreach (var row in Rows)
        {
            if (row.IsEmpty) continue;
            int? slot = position < 20 ? position + 1 : null;
            position++;

            var entry = row.ToEntry(slot);
            if (row.IsSongOverride) _document.EmojiEntries.Add(entry);
            else _settings.GlobalEmojiList.Add(entry);
        }

        _settings.Save();
        SongListChanged = true;
    }

    // ------------------------------------------------------------ 行操作

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        var row = new EmojiSlotRow();
        Rows.Add(row);
        UpdateKeyLabels();
        SlotList.SelectedItem = row;
        SlotList.ScrollIntoView(row);
    }

    private void OnDuplicateRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EmojiSlotRow row) return;
        var copy = row.Clone();
        int index = Rows.IndexOf(row);
        Rows.Insert(index + 1, copy);
        UpdateKeyLabels();
        SlotList.SelectedItem = copy;
        SlotList.ScrollIntoView(copy);
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EmojiSlotRow row) return;
        Rows.Remove(row);
        if (Rows.Count == 0) Rows.Add(new EmojiSlotRow());
        UpdateKeyLabels();
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => MoveRow(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => MoveRow(sender, +1);

    private void MoveRow(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not EmojiSlotRow row) return;
        int index = Rows.IndexOf(row);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Rows.Count) return;
        Rows.Move(index, target);
        UpdateKeyLabels();
        SlotList.SelectedItem = row;
    }

    // ------------------------------------------------------------ プレビュー

    private void OnRowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewRow is not null) _previewRow.PropertyChanged -= OnPreviewRowChanged;
        _previewRow = SlotList.SelectedItem as EmojiSlotRow;
        if (_previewRow is not null) _previewRow.PropertyChanged += OnPreviewRowChanged;
        RenderPreview();
    }

    private void OnPreviewRowChanged(object? sender, PropertyChangedEventArgs e) => RenderPreview();

    /// <summary>選択行を、現在のフォント設定・Zoom・Margin を反映した縮小スケールで描画する。</summary>
    private void RenderPreview()
    {
        PreviewHost.Children.Clear();
        if (_previewRow is not EmojiSlotRow row)
        {
            PreviewCaption.Text = "行を選択するとプレビューを表示します";
            return;
        }

        var opts = row.ToEntry(null).ParseOptions();
        double baseFontPx = Math.Max(8, _settings.FontSizePx);
        double scale = Math.Min(1.0, 48.0 / baseFontPx);
        double fontPx = baseFontPx * scale;
        double iconHeight = fontPx * opts.ZoomPercent / 100.0;

        var font = new Microsoft.UI.Xaml.Media.FontFamily(_settings.FontFamily);
        var weight = _settings.FontBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

        void AddText(string text) => PreviewHost.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = fontPx,
            FontFamily = font,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        void AddIcon(string path, string fallback)
        {
            if (File.Exists(path))
            {
                PreviewHost.Children.Add(new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)),
                    Height = iconHeight,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(
                        Math.Max(0, opts.MarginLeft) * scale, 0,
                        Math.Max(0, opts.MarginRight) * scale,
                        opts.MarginBottom * scale),
                });
            }
            else
            {
                AddText(fallback);
            }
        }

        AddText("歌詞");
        AddIcon(row.ImageBefore.Trim(), row.ReplaceChar.Length > 0 ? row.ReplaceChar : "（画像なし）");
        AddText("の続き");

        if (!string.IsNullOrWhiteSpace(row.ImageAfter))
        {
            AddText("　ワイプ後: ");
            AddIcon(row.ImageAfter.Trim(), "（画像なし）");
        }

        PreviewCaption.Text =
            $"プレビュー: {_settings.FontFamily} {baseFontPx:F0}px を {scale * 100:F0}% に縮小表示　" +
            $"Zoom={opts.ZoomPercent:F0}%　MarginL/R/B={opts.MarginLeft:F0}/{opts.MarginRight:F0}/{opts.MarginBottom:F0}" +
            (opts.Fix ? "　Fix(元サイズ・実寸は反映されません)" : "") +
            (opts.NoDecor ? "　NoDecor" : "");
    }

    // ------------------------------------------------------------ 画像参照

    private async void OnBrowseBeforeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EmojiSlotRow row) return;
        string? path = await PickImageAsync();
        if (path is not null) row.ImageBefore = path;
    }

    private async void OnBrowseAfterClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EmojiSlotRow row) return;
        string? path = await PickImageAsync();
        if (path is not null) row.ImageAfter = path;
    }

    private async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker();
        if (App.MainWindow is not null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        }
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
