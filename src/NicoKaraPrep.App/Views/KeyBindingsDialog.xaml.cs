using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Services;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 挿入ビューの機能キー割り当てをキーボード配列で表示・変更するダイアログ。
/// キーをクリック → 機能を選択で割り当てる（既存の割り当てとは入れ替え）。
/// </summary>
public sealed partial class KeyBindingsDialog : ContentDialog
{
    private readonly Dictionary<InsertViewAction, string> _map;
    private readonly Dictionary<string, TextBlock> _captions = new();

    /// <summary>編集結果（OK 時に参照する。機能 → キー ID）。</summary>
    public Dictionary<InsertViewAction, string> Result => _map;

    public KeyBindingsDialog(Dictionary<InsertViewAction, string> current)
    {
        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 980d;
        _map = new Dictionary<InsertViewAction, string>(current);
        BuildKeyboard();
        RefreshCaptions();
    }

    // ------------------------------------------------------------ キーボード構築

    private void BuildKeyboard()
    {
        KeyboardHost.Children.Clear();
        _captions.Clear();

        AddRow(0, new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }
            .Select(k => MakeFixedKey(k, "スロット")));
        AddRow(24, new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" }
            .Select(k => MakeFixedKey(k, "スロット")));
        AddRow(36, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" }
            .Select(k => MakeAssignableKey(k)));
        AddRow(52, new[] { "Z", "X", "C", "V", "B", "N", "M", "Slash", "Backslash" }
            .Select(k => MakeAssignableKey(k)));

        var spaceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(120, 0, 0, 0) };
        spaceRow.Children.Add(MakeAssignableKey("Space", width: 230));
        spaceRow.Children.Add(MakeFixedKey("BS / Del", "絵文字・空白削除", width: 130));
        spaceRow.Children.Add(MakeFixedKey("Esc", "ビュー終了", width: 90));
        KeyboardHost.Children.Add(spaceRow);
    }

    private void AddRow(double indent, IEnumerable<UIElement> keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(indent, 0, 0, 0) };
        foreach (var k in keys) row.Children.Add(k);
        KeyboardHost.Children.Add(row);
    }

    private static UIElement MakeFixedKey(string label, string caption, double width = 72)
    {
        var content = MakeKeyContent(label, out var cap);
        cap.Text = caption;
        return new Button
        {
            Width = width,
            Height = 56,
            IsEnabled = false,
            Padding = new Thickness(2),
            Content = content,
        };
    }

    private UIElement MakeAssignableKey(string keyId) => MakeAssignableKey(keyId, 72);

    private UIElement MakeAssignableKey(string keyId, double width)
    {
        var button = new Button
        {
            Width = width,
            Height = 56,
            Padding = new Thickness(2),
            Content = MakeKeyContent(InsertViewKeyMap.KeyLabel(keyId), out var cap),
        };
        _captions[keyId] = cap;

        var flyout = new MenuFlyout();
        foreach (var info in InsertViewKeyMap.Actions)
        {
            var item = new MenuFlyoutItem { Text = info.Name };
            var action = info.Action;
            item.Click += (_, _) => Assign(action, keyId);
            flyout.Items.Add(item);
        }
        button.Flyout = flyout;
        return button;
    }

    private static StackPanel MakeKeyContent(string label, out TextBlock caption)
    {
        caption = new TextBlock
        {
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(caption);
        return panel;
    }

    // ------------------------------------------------------------ 割り当て

    /// <summary>機能 action をキー keyId へ割り当てる（既存の割り当てとは入れ替え）。</summary>
    private void Assign(InsertViewAction action, string keyId)
    {
        var currentOwner = _map.FirstOrDefault(kv => kv.Value == keyId);
        string oldKey = _map[action];

        _map[action] = keyId;
        if (currentOwner.Value == keyId && currentOwner.Key != action)
        {
            _map[currentOwner.Key] = oldKey; // 入れ替え
        }
        RefreshCaptions();
    }

    private void RefreshCaptions()
    {
        var byKey = _map.ToDictionary(kv => kv.Value, kv => kv.Key);
        foreach (var (keyId, caption) in _captions)
        {
            caption.Text = byKey.TryGetValue(keyId, out var action)
                ? InsertViewKeyMap.Actions.First(a => a.Action == action).ShortName
                : "─";
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _map.Clear();
        foreach (var info in InsertViewKeyMap.Actions)
        {
            _map[info.Action] = info.DefaultKey;
        }
        RefreshCaptions();
    }
}
