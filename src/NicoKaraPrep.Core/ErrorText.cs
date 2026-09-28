namespace NicoKaraPrep.Core;

/// <summary>例外を画面に出す説明文にする。</summary>
public static class ErrorText
{
    /// <summary>
    /// 例外の説明（1 行）。WinRT の COMException はメッセージが空のことがあるため、
    /// メッセージが無いときはよくあるエラーコードを日本語で説明し、最後に例外の種類とエラーコードを付ける。
    /// </summary>
    public static string Describe(Exception ex)
    {
        string message = ex.Message.Trim();
        if (message.Length == 0) message = KnownMessage(ex.HResult) ?? "詳しい内容は取得できませんでした";
        return $"{message}（{ex.GetType().Name}、エラーコード 0x{ex.HResult:X8}）";
    }

    private static string? KnownMessage(int hresult) => unchecked((uint)hresult) switch
    {
        0x80070002 => "ファイルが見つかりません",
        0x80070003 => "フォルダが見つかりません",
        0x80070005 => "アクセスが拒否されました",
        0x80070020 => "ほかのアプリがファイルを使用中です",
        0x800700CE => "ファイルのパスが長すぎます",
        _ => null,
    };
}
