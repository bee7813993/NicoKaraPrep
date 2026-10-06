using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 字幕アクションの曲の既定（<see cref="Core.Project.N3ProjSongSettings.SubtitleAction"/>。null = 自動）の種類と設定値を選ぶポップアップ。
/// 行リストの右のパネル「字幕アクション」の「曲の既定を設定...」・エクスポート(X) から開く。写しを編集し、OK のときだけ
/// <see cref="Result"/> を返す（書き込みはメイン画面が <see cref="MainViewModel.SetSongDefaultSubtitleAction"/> で行う）。
/// </summary>
public sealed partial class SongActionDialog : ContentDialog
{
    private readonly MainViewModel _vm;

    /// <summary>自動のときに使うアクション（ベース → ニコカラメーカー3 の設定 → 文字単位フェード）。</summary>
    private readonly N3SubtitleAction _auto;

    private readonly N3SubtitleActionSource _autoSource;

    /// <summary>編集中の曲の既定（null = 自動）。</summary>
    private N3SubtitleAction? _working;

    /// <summary>種類の欄の項目の Id（先頭の null は自動）。</summary>
    private readonly List<string?> _kindIds = new();

    /// <summary>欄に値を入れている最中か（そのあいだの欄の変化は編集として扱わない）。</summary>
    private bool _loading;

    public SongActionDialog(MainViewModel vm)
    {
        _vm = vm;
        _auto = vm.ResolveAutoSubtitleAction(out _autoSource);
        _working = vm.N3ProjSettings.SubtitleAction is { Id.Length: > 0 } song ? song.Clone() : null;
        InitializeComponent();
        LoadKinds();
        BuildFields();
        PrimaryButtonClick += (_, _) => Result = _working?.Clone();
    }

    /// <summary>OK で閉じたときの曲の既定（null = 自動）。</summary>
    public N3SubtitleAction? Result { get; private set; }

    /// <summary>大きさ（px）の項目に使う画面の高さ（書き出すプロジェクトの画面の高さ）。</summary>
    private int ScreenHeight => _vm.GetScreenSize().Height;

    /// <summary>ニコカラメーカー3 の AddOns の値（無ければ null）。</summary>
    private JsonObject? AddOn(string id) => _vm.Nkm3Env?.AddOnSettings.GetValueOrDefault(id);

    /// <summary>種類の欄（自動と 8 種類。知らない Id の今の既定は、表示だけする）と、自動の説明を作る。</summary>
    private void LoadKinds()
    {
        string autoName = _vm.DescribeSubtitleAction(_auto);
        string sourceText = _autoSource switch
        {
            N3SubtitleActionSource.Base => "ベースのまま",
            N3SubtitleActionSource.Nkm3 => "ニコカラメーカー3 の設定",
            _ => "既定",
        };
        var names = new List<string>
        {
            _autoSource is N3SubtitleActionSource.Base or N3SubtitleActionSource.Nkm3 ? $"（自動: {sourceText} → {autoName}）" : $"（自動: {autoName}）",
        };
        _kindIds.Add(null);
        foreach (var kind in N3SubtitleActionCatalog.Known)
        {
            names.Add(kind.Name);
            _kindIds.Add(kind.Id);
        }
        if (_working is { } w && !N3SubtitleActionCatalog.IsKnown(w.Id))
        {
            // 知らない Id（新しい版のニコカラメーカー3 のものなど）は表示だけする（値はそのまま書き出す）
            names.Add(N3SubtitleActionCatalog.DisplayName(w.Id));
            _kindIds.Add(w.Id);
        }
        _loading = true;
        try
        {
            KindBox.ItemsSource = names;
            KindBox.SelectedIndex = _working is null ? 0 : Math.Max(0, _kindIds.IndexOf(_working.Id));
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || KindBox.SelectedIndex < 0) return;
        string? id = _kindIds[KindBox.SelectedIndex];
        if (id is null)
        {
            _working = null;
        }
        else if (_working?.Id != id)
        {
            // 自動で決まるものと同じ種類ならその値から、違えばニコカラメーカー3 の設定の値（無ければ既定値）から始める
            _working = _auto.Id == id ? _auto.Clone() : N3SubtitleActionCatalog.CreateDefault(id, AddOn(id));
        }
        BuildFields();
    }

    /// <summary>「ニコカラメーカー3 の設定に戻す」。</summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_working is not { } w || !N3SubtitleActionCatalog.IsKnown(w.Id)) return;
        _working = N3SubtitleActionCatalog.CreateDefault(w.Id, AddOn(w.Id));
        BuildFields();
    }

    /// <summary>「ニコカラメーカー3 の設定に戻す」を押せるか（種類を選んでいて、値がニコカラメーカー3 の設定の値と違う）と、説明を合わせる。</summary>
    private void UpdateState()
    {
        ResetButton.IsEnabled = _working is { } w && N3SubtitleActionCatalog.IsKnown(w.Id)
            && !w.SameAs(N3SubtitleActionCatalog.CreateDefault(w.Id, AddOn(w.Id)));
        string autoName = _vm.DescribeSubtitleAction(_auto);
        string note = _working is null
            ? _autoSource switch
            {
                N3SubtitleActionSource.Base => $"自動のときは、ベースの n3proj の歌詞行でいちばん多い字幕アクション（{autoName}）を使います。",
                N3SubtitleActionSource.Nkm3 => $"自動のときは、ニコカラメーカー3 の「すべて同じ字幕アクションにする」の設定（{autoName}）を使います（ベースの n3proj に字幕アクションが無いため）。",
                _ => "自動のときは、文字単位フェードを使います（ベースの n3proj にもニコカラメーカー3 の設定にも字幕アクションが無いため）。",
            }
            : $"行ごとに指定していない歌詞行に、{_vm.DescribeSubtitleAction(_working)} を書き出します。";
        if (!_vm.CanSaveSongFontSets) note += "\n歌詞ファイルを保存していないため、曲の既定は .tttproj に保存されません（歌詞ファイルを保存すると保存します）。";
        NoteText.Text = note;
    }

    /// <summary>
    /// 選んだ種類の設定欄を作る（カタログの項目から: 時間は ms の数値の欄、大きさは px の数値の欄、する・しないはチェック）。
    /// 自動のときは、自動で決まるアクションの値を変えられない形で出す。
    /// </summary>
    private void BuildFields()
    {
        NumbersGrid.Children.Clear();
        NumbersGrid.RowDefinitions.Clear();
        ChecksPanel.Children.Clear();
        bool isAuto = _working is null;
        var shown = _working ?? _auto;
        var kind = N3SubtitleActionCatalog.Find(shown.Id);
        var fields = kind?.VisibleFields.ToList() ?? new List<N3SubtitleActionField>();
        string fieldsNote = kind is null
            ? "設定項目の分からない字幕アクションです（値はそのまま書き出します）"
            : fields.Count == 0 ? "この字幕アクションに設定項目はありません"
            : isAuto ? "自動のあいだは値を変えられません（種類を選ぶと変えられます）" : "";
        FieldsNote.Text = fieldsNote;
        FieldsNote.Visibility = fieldsNote.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _loading = true;
        try
        {
            int numbers = 0;
            int height = ScreenHeight;
            foreach (var field in fields)
            {
                if (field.Kind == N3SubtitleActionFieldKind.Bool)
                {
                    bool value = shown.GetBool(field.Key) ?? Convert.ToBoolean(field.Default, CultureInfo.InvariantCulture);
                    var check = new CheckBox { Content = new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, IsEnabled = !isAuto };
                    AutomationProperties.SetName(check, field.Label);
                    string onOff = field.TrueText.Length > 0 || field.FalseText.Length > 0 ? $"（オン: {field.TrueText}／オフ: {field.FalseText}）" : "";
                    ToolTipService.SetToolTip(check, field.Label + onOff);
                    check.Click += (_, _) =>
                    {
                        if (_loading || _working is null) return;
                        _working.Set(field.Key, check.IsChecked == true);
                        UpdateState();
                    };
                    ChecksPanel.Children.Add(check);
                    continue;
                }

                bool pixels = field.Kind == N3SubtitleActionFieldKind.Pixels;
                double current = pixels
                    ? shown.GetPixels(field.Key, height) ?? Convert.ToDouble(field.Default, CultureInfo.InvariantCulture) * height / N3SubtitleActionCatalog.PixelsReference
                    : shown.GetInt(field.Key) ?? Convert.ToInt32(field.Default, CultureInfo.InvariantCulture);
                string unit = pixels ? "px" : "ms";
                var box = new NumberBox
                {
                    Header = new TextBlock { Text = $"{field.Label}（{unit}）", TextWrapping = TextWrapping.Wrap },
                    Value = Math.Round(current),
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    SmallChange = pixels ? 5 : 10,
                    LargeChange = pixels ? 20 : 50,
                    Minimum = pixels ? -10000 : 0,
                    Maximum = pixels ? 10000 : 60000,
                    IsEnabled = !isAuto,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                AutomationProperties.SetName(box, $"{field.Label}（{unit}）");
                ToolTipService.SetToolTip(box, pixels
                    ? $"{field.Label}（書き出すプロジェクトの画面の高さ {height} での px）"
                    : $"{field.Label}（ミリ秒）");
                box.ValueChanged += (sender, args) =>
                {
                    if (_loading || _working is null) return;
                    if (!double.IsFinite(args.NewValue))
                    {
                        // 空にした欄は今の値に戻す（欄の処理が終わってから）
                        DispatcherQueue.TryEnqueue(() => RestoreNumber(sender, field));
                        return;
                    }
                    if (pixels) _working.SetPixels(field.Key, args.NewValue, ScreenHeight);
                    else _working.Set(field.Key, (int)Math.Round(Math.Max(0, args.NewValue), MidpointRounding.AwayFromZero));
                    UpdateState();
                };
                if (numbers % 2 == 0) NumbersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(box, numbers / 2);
                Grid.SetColumn(box, numbers % 2);
                NumbersGrid.Children.Add(box);
                numbers++;
            }
        }
        finally
        {
            _loading = false;
        }
        UpdateState();
    }

    /// <summary>空にした数値の欄を、今の値に戻す。</summary>
    private void RestoreNumber(NumberBox box, N3SubtitleActionField field)
    {
        if (_working is not { } w) return;
        double? value = field.Kind == N3SubtitleActionFieldKind.Pixels ? w.GetPixels(field.Key, ScreenHeight) : w.GetInt(field.Key);
        if (value is null) return;
        _loading = true;
        try
        {
            box.Value = Math.Round(value.Value);
        }
        finally
        {
            _loading = false;
        }
    }
}
