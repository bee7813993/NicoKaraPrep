using System.Runtime.InteropServices;

namespace NicoKaraPrep.Core.Tests;

public class ErrorTextTests
{
    [Fact]
    public void メッセージが空のCOMエラーは種類とエラーコードを出す()
    {
        var ex = new COMException("", unchecked((int)0x80004005));
        Assert.Equal("詳しい内容は取得できませんでした（COMException、エラーコード 0x80004005）", ErrorText.Describe(ex));
    }

    [Fact]
    public void メッセージが空でもよくあるエラーコードは日本語で説明する()
    {
        var ex = new COMException("  ", unchecked((int)0x80070005));
        Assert.Equal("アクセスが拒否されました（COMException、エラーコード 0x80070005）", ErrorText.Describe(ex));
    }

    [Fact]
    public void メッセージがあればそのまま使う()
    {
        var ex = new InvalidDataException("ZIP が壊れています");
        Assert.StartsWith("ZIP が壊れています（InvalidDataException、エラーコード 0x", ErrorText.Describe(ex));
    }
}
