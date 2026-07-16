namespace TimeTagTool.Core.Model;

/// <summary>
/// @タグ 1 件（@Ruby / @Emoji 以外）。原文の順序を保って保持する。
/// </summary>
public sealed class MetadataTag
{
    public MetadataTag(string name, string value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>タグ名（@ を除く。例: "Title"）。</summary>
    public string Name { get; set; }

    /// <summary>タグの内容。</summary>
    public string Value { get; set; }

    public string ToLine() => $"@{Name}={Value}";
}
