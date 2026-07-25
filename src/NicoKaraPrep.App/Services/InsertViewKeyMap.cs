using Windows.System;

namespace NicoKaraPrep.App.Services;

/// <summary>挿入ビューでキーに割り当てられる機能。</summary>
public enum InsertViewAction
{
    PlaceholderInsert,
    LeadTagInsert,
    SpaceInsert,
    PlayPause,
    PlayFromCursor,
    Play,
    Pause,
    SeekBack,
    SeekForward,
    FollowToggle,
    SplitLine,
    JoinLine,
}

/// <summary>機能 1 件の表示情報と既定キー。</summary>
/// <param name="Action">機能。</param>
/// <param name="Name">設定画面・凡例用の名前。</param>
/// <param name="ShortName">キーキャップ表示用の短い名前。</param>
/// <param name="DefaultKey">既定のキー ID。</param>
public sealed record InsertViewActionInfo(InsertViewAction Action, string Name, string ShortName, string DefaultKey);

/// <summary>
/// 挿入ビューの機能キー割り当ての既定値・正規化・キー変換。
/// キー ID は "A"–"Z"（スロットで使う Q–P 行と 1–0 を除く）、"Space"、"Slash"、"Backslash"。
/// </summary>
public static class InsertViewKeyMap
{
    /// <summary>割り当て可能な機能（凡例の表示順）。</summary>
    public static readonly IReadOnlyList<InsertViewActionInfo> Actions = new InsertViewActionInfo[]
    {
        new(InsertViewAction.PlaceholderInsert, "プレースホルダ（＿）挿入", "＿挿入", "B"),
        new(InsertViewAction.LeadTagInsert, "先行タグ挿入", "先行タグ", "G"),
        new(InsertViewAction.SpaceInsert, "空白挿入（Shift で全角）", "空白", "K"),
        new(InsertViewAction.PlayPause, "再生 / 一時停止", "再/停", "Space"),
        new(InsertViewAction.PlayFromCursor, "カーソル位置から再生", "ここから", "D"),
        new(InsertViewAction.Play, "再生", "再生", "A"),
        new(InsertViewAction.Pause, "一時停止", "停止", "S"),
        new(InsertViewAction.SeekBack, "数秒戻る", "←戻る", "Z"),
        new(InsertViewAction.SeekForward, "数秒進む", "進む→", "X"),
        new(InsertViewAction.FollowToggle, "再生追従カーソル", "追従", "F"),
        new(InsertViewAction.SplitLine, "行分割", "分割", "Slash"),
        new(InsertViewAction.JoinLine, "行結合（Shift でスペースを挟む）", "結合", "Backslash"),
    };

    /// <summary>割り当てに使えるキー ID（キーボード配列順）。</summary>
    public static readonly IReadOnlyList<string> AssignableKeys = new[]
    {
        "A", "S", "D", "F", "G", "H", "J", "K", "L",
        "Z", "X", "C", "V", "B", "N", "M", "Slash", "Backslash",
        "Space",
    };

    /// <summary>キー ID → キーキャップ表示ラベル。</summary>
    public static string KeyLabel(string keyId) => keyId switch
    {
        "Slash" => "/",
        "Backslash" => "＼",
        "Space" => "Space",
        _ => keyId,
    };

    /// <summary>キー ID → 対応する VirtualKey（Backslash は JIS の ￥ と ろ の 2 つ）。</summary>
    public static IEnumerable<VirtualKey> ToVirtualKeys(string keyId)
    {
        switch (keyId)
        {
            case "Space":
                yield return VirtualKey.Space;
                break;
            case "Slash":
                yield return (VirtualKey)191; // OEM_2（JIS の め）
                break;
            case "Backslash":
                yield return (VirtualKey)220; // OEM_5（JIS の ￥）
                yield return (VirtualKey)226; // OEM_102（JIS の ろ）
                break;
            default:
                if (keyId.Length == 1 && keyId[0] is >= 'A' and <= 'Z')
                {
                    yield return VirtualKey.A + (keyId[0] - 'A');
                }
                break;
        }
    }

    /// <summary>
    /// 設定の割り当て辞書を正規化する（不足の補完・不正キーや重複の既定戻し）。
    /// 戻り値は 機能 → キー ID の完全な辞書。
    /// </summary>
    public static Dictionary<InsertViewAction, string> Normalize(Dictionary<string, string> stored)
    {
        var result = new Dictionary<InsertViewAction, string>();
        var usedKeys = new HashSet<string>();

        // 保存値のうち有効なもの（既知の機能・使用可能キー・重複なし）を先に採用
        foreach (var info in Actions)
        {
            if (stored.TryGetValue(info.Action.ToString(), out string? key) &&
                key is not null && AssignableKeys.Contains(key) && usedKeys.Add(key))
            {
                result[info.Action] = key;
            }
        }

        // 残りは既定キー → 空いているキーの順で補完
        foreach (var info in Actions)
        {
            if (result.ContainsKey(info.Action)) continue;
            string key = usedKeys.Add(info.DefaultKey)
                ? info.DefaultKey
                : AssignableKeys.First(k => usedKeys.Add(k));
            result[info.Action] = key;
        }
        return result;
    }

    /// <summary>正規化済みの辞書を設定へ保存する形（文字列キー）に変換する。</summary>
    public static Dictionary<string, string> ToStored(Dictionary<InsertViewAction, string> map) =>
        map.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
}
