namespace TimeTagTool.Core.Model;

/// <summary>
/// 歌詞 1 文字分の単位。RhythmicaLyrics の内部構造（mojitan / t_jikan / t_kazu / sakura_yomi）に対応する。
/// サロゲートペアは 1 単位として Text に保持する。
/// </summary>
public sealed class CharUnit
{
    /// <summary>2連タグ（同一位置への連続タイムタグ）用スペーサー文字（RhythmicaLyrics 内部の 0x1A）。</summary>
    public const string Spacer = SpacerCharConst;
    private const string SpacerCharConst = "\u001A";

    /// <summary>文字（1 コードポイント。スペーサーの場合は <see cref="Spacer"/>）。</summary>
    public string Text { get; set; } = "";

    /// <summary>この文字に打たれたタイムタグ時刻（10ms 単位）。null はタグなし。</summary>
    public int? TimeCs { get; set; }

    /// <summary>チェック数（RhythmicaLyrics の t_kazu。0 はチェックなし）。</summary>
    public int CheckCount { get; set; }

    /// <summary>この文字に割り当てられたルビ（読み）。null はルビなし。空文字はルビグループ内の継続文字。</summary>
    public string? Ruby { get; set; }

    /// <summary>ルビグループが次の文字へ続く（テキスト編集モードの ＋ 連結に対応）。</summary>
    public bool RubyJoinsNext { get; set; }

    /// <summary>口パク用補助タイムタグ（RhythmicaLyrics の sakura_timetag。チェック 2 個目以降の空打ち時間）。</summary>
    public List<int> AuxTimeTagsCs { get; } = new();

    /// <summary>rlf の mojitan_width（文字描画幅キャッシュ）。ラウンドトリップ用。</summary>
    public int WidthCache { get; set; }

    /// <summary>rlf の sakura_surface（FLELE 用）。通常 null。ラウンドトリップ用。</summary>
    public string? SakuraSurface { get; set; }

    /// <summary>rlf の sakura_script（FLELE 用）。通常 null。ラウンドトリップ用。</summary>
    public string? SakuraScript { get; set; }

    /// <summary>2連タグ用スペーサーかどうか。</summary>
    public bool IsSpacer => Text == Spacer;

    /// <summary>ルビ情報（自身のルビまたはグループ継続）を持つか。</summary>
    public bool HasRubyInfo => Ruby is not null;

    public CharUnit Clone()
    {
        var c = new CharUnit
        {
            Text = Text,
            TimeCs = TimeCs,
            CheckCount = CheckCount,
            Ruby = Ruby,
            RubyJoinsNext = RubyJoinsNext,
            WidthCache = WidthCache,
            SakuraSurface = SakuraSurface,
            SakuraScript = SakuraScript,
        };
        c.AuxTimeTagsCs.AddRange(AuxTimeTagsCs);
        return c;
    }

    public override string ToString() =>
        $"'{Text}'{(TimeCs is int t ? TimeTag.Format(t) : "")}{(CheckCount > 0 ? $" chk={CheckCount}" : "")}{(Ruby is not null ? $" ruby={Ruby}" : "")}";
}
