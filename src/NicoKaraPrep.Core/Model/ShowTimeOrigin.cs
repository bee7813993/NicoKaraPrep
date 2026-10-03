using System.Text.Json.Serialization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 行の表示時刻（<see cref="LyricsLine.ShowBeginCs"/>・<see cref="LyricsLine.ShowEndCs"/>）を、どこから決めたか。
/// 値を持たない（null の）表示時刻は、書き出しや画面の表示のたびに自動で計算する。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShowTimeOrigin
{
    /// <summary>手で指定した（行設定の欄など）。自動調整を実行し直しても変えない（以前の版の手動指定もこれ）。</summary>
    Manual,

    /// <summary>プロジェクト（n3proj）から読み込んだ値。自動調整を実行すると計算し直す。</summary>
    Loaded,

    /// <summary>自動調整を実行して決めた値。自動調整を実行し直すと計算し直す。</summary>
    Auto,
}
