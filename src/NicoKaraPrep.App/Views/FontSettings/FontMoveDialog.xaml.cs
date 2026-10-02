using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// 「移動...」の移動先を選ぶ画面。アプリ共通の階層のフォルダとフォント設定を字下げして並べ、
/// 検索すると当てはまるものだけを上の階層の名前つきで平らに並べる。選んだら <see cref="SelectedTarget"/> に入る。
/// </summary>
public sealed partial class FontMoveDialog : ContentDialog
{
    private readonly IReadOnlyList<FontMoveTarget> _targets;

    /// <param name="label">移すもの（フォント設定・フォルダ）の名前。</param>
    /// <param name="targets">移し先（先頭が「いちばん上の階層」。<see cref="FontSettingsViewModel.MoveTargets"/>）。</param>
    public FontMoveDialog(string label, IReadOnlyList<FontMoveTarget> targets)
    {
        _targets = targets;
        InitializeComponent();
        Title = $"「{label}」の移動先";
        ApplySearch();
        PrimaryButtonClick += (_, _) => SelectedTarget = TargetList.SelectedItem as FontMoveTarget;
        Opened += (_, _) => TargetSearchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    /// <summary>選んだ移し先（キャンセルなら null）。</summary>
    public FontMoveTarget? SelectedTarget { get; private set; }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        string q = TargetSearchBox.Text.Trim();
        TargetList.ItemsSource = q.Length == 0
            ? _targets.ToList()
            : _targets
                .Where(t => t.Key is not null && t.Label.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(t => t with { Depth = 0, ShowPath = t.Path.Length > 0 })
                .ToList();
        IsPrimaryButtonEnabled = false;
    }

    private void OnTargetSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = TargetList.SelectedItem is FontMoveTarget;

    private void OnTargetDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TargetList.SelectedItem is not FontMoveTarget target) return;
        SelectedTarget = target;
        Hide();
    }
}
