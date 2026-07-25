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

        // 実際のキーボードの位置に合わせる: Esc は左上、BS は数字行の右端、
        // Del は Q 行の右端（ナビキー島の位置）、方向キーは右下
        AddRow(0, new[] { MakeFixedKey("Esc", "ビュー終了") });

        var digitRow = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }
            .Select(k => MakeFixedKey(k, "スロット"))
            .Append(MakeFixedKey("BS", "絵文字・空白削除", width: 96));
        AddRow(0, digitRow);

        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(24, 0, 0, 0) };
        foreach (string k in new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" })
        {
            qRow.Children.Add(MakeFixedKey(k, "スロット"));
        }
        var delKey = (FrameworkElement)MakeFixedKey("Del", "絵文字・空白削除", width: 96);
        delKey.Margin = new Thickness(28, 0, 0, 0);
        qRow.Children.Add(delKey);
        KeyboardHost.Children.Add(qRow);

        AddRow(36, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" }
            .Select(k => MakeAssignableKey(k)));
        AddRow(52, new[] { "Z", "X", "C", "V", "B", "N", "M", "Slash", "Backslash" }
            .Select(k => MakeAssignableKey(k)));

        var spaceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(120, 0, 0, 0) };
        spaceRow.Children.Add(MakeAssignableKey("Space", width: 230));
        spaceRow.Children.Add(MakeArrowCluster());
        KeyboardHost.Children.Add(spaceRow);
    }

    /// <summary>方向キー（カーソル移動）の説明表示。逆 T 字の配置で並べる。</summary>
    private static UIElement MakeArrowCluster()
    {
        var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static UIElement Key(string label) => new Button
        {
            Width = 44,
            Height = 26,
            IsEnabled = false,
            Padding = new Thickness(0),
            Content = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
        };

        void Add(UIElement el, int row, int col)
        {
            Grid.SetRow((FrameworkElement)el, row);
            Grid.SetColumn((FrameworkElement)el, col);
            grid.Children.Add(el);
        }
        Add(Key("↑"), 0, 1);
        Add(Key("←"), 1, 0);
        Add(Key("↓"), 1, 1);
        Add(Key("→"), 1, 2);

        var panel = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock
        {
            Text = "カーソル移動",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return panel;
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
