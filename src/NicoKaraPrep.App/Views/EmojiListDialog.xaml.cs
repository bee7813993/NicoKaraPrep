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
    public EmojiSlotRow(int slot)
    {
        Slot = slot;
    }

    public int Slot { get; }

    public string KeyLabel => EmojiSlotViewModel.KeyLabels[Slot - 1];

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

    public EmojiEntry ToEntry() => new()
    {
        ReplaceChar = ReplaceChar.Trim(),
        ImageBefore = ImageBefore.Trim(),
        ImageAfter = string.IsNullOrWhiteSpace(ImageAfter) ? null : ImageAfter.Trim(),
        Options = string.IsNullOrWhiteSpace(Options) ? null : Options.Trim(),
        Slot = Slot,
    };

    public void LoadFrom(EmojiEntry e, bool songOverride)
    {
        ReplaceChar = e.ReplaceChar;
        ImageBefore = e.ImageBefore;
        ImageAfter = e.ImageAfter ?? "";
        Options = e.Options ?? "";
        IsSongOverride = songOverride;
    }
}

public sealed partial class EmojiListDialog : ContentDialog
{
    private readonly AppSettings _settings;
    private readonly LyricsDocument _document;

    public List<EmojiSlotRow> Rows { get; } = new();

    /// <summary>OK で曲側のリストが変更されたかどうか。</summary>
    public bool SongListChanged { get; private set; }

    public EmojiListDialog(AppSettings settings, LyricsDocument document)
    {
        _settings = settings;
        _document = document;

        for (int i = 1; i <= 20; i++)
        {
            var row = new EmojiSlotRow(i);
            var song = document.EmojiEntries.FirstOrDefault(e => e.Slot == i);
            var global = settings.GlobalEmojiList.FirstOrDefault(e => e.Slot == i);
            if (song is not null) row.LoadFrom(song, songOverride: true);
            else if (global is not null) row.LoadFrom(global, songOverride: false);
            Rows.Add(row);
        }

        InitializeComponent();
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void Apply()
    {
        bool songChanged = false;

        foreach (var row in Rows)
        {
            var oldSong = _document.EmojiEntries.FirstOrDefault(e => e.Slot == row.Slot);
            var oldGlobal = _settings.GlobalEmojiList.FirstOrDefault(e => e.Slot == row.Slot);

            if (oldSong is not null) { _document.EmojiEntries.Remove(oldSong); songChanged = true; }
            if (oldGlobal is not null) _settings.GlobalEmojiList.Remove(oldGlobal);

            if (row.IsEmpty) continue;

            if (row.IsSongOverride)
            {
                _document.EmojiEntries.Add(row.ToEntry());
                songChanged = true;
            }
            else
            {
                _settings.GlobalEmojiList.Add(row.ToEntry());
            }
        }

        _settings.Save();
        SongListChanged = songChanged;
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
