using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>@Emoji パレットの 1 スロット（1–20）。</summary>
public partial class EmojiSlotViewModel : ObservableObject
{
    /// <summary>スロット番号 → キー表示（1–0, Q–P）。</summary>
    public static readonly string[] KeyLabels =
    [
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
    ];

    public EmojiSlotViewModel(int slot)
    {
        Slot = slot;
    }

    /// <summary>スロット番号（1–20）。</summary>
    public int Slot { get; }

    public string KeyLabel => KeyLabels[Slot - 1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplaceChar))]
    [NotifyPropertyChangedFor(nameof(ImagePath))]
    [NotifyPropertyChangedFor(nameof(HasEntry))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    [NotifyPropertyChangedFor(nameof(Thumbnail))]
    private EmojiEntry? entry;

    /// <summary>ワイプ前画像のサムネイル。</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Thumbnail =>
        ImagePath is string p ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p)) : null;

    /// <summary>このスロットが曲ごとの上書きかどうか（表示用）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool isSongOverride;

    public string ReplaceChar => Entry?.ReplaceChar ?? "";

    public string? ImagePath
    {
        get
        {
            string? p = Entry?.ImageBefore;
            return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
        }
    }

    public bool HasEntry => Entry is not null && !string.IsNullOrEmpty(Entry.ReplaceChar);

    public string ToolTipText => Entry is null
        ? $"スロット {KeyLabel}（未設定）"
        : $"スロット {KeyLabel}: {Entry.ReplaceChar}\n{Entry.ImageBefore}\n" +
          (IsSongOverride
              ? "この曲だけの絵文字（歌詞ファイルの @Emoji として保存。新規作成・ほかの曲を開くと出なくなります）"
              : "アプリ共通の絵文字（設定に保存。どの曲でも使えます）");
}
