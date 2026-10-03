using System.Reflection;
using ModelContextProtocol.Server;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// MCP で公開する道具の一覧。定義は <see cref="NkpTools"/> の属性付きのメソッド 1 つ。
/// 本体（<see cref="McpHost"/>）は本物の <see cref="MainWindow"/> と組にして作り、橋渡し（<see cref="McpBridge"/>）は
/// 本体が無いときの一覧のために window 無しで作る（呼ばれることはない）。
/// </summary>
internal static class McpToolCatalog
{
    public static IReadOnlyList<McpServerTool> Create(MainWindow? window)
    {
        var target = new NkpTools(window);
        var tools = new List<McpServerTool>();
        foreach (var method in typeof(NkpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null) continue;
            tools.Add(McpServerTool.Create(method, target, new McpServerToolCreateOptions
            {
                SerializerOptions = McpSerializer.Options,
            }));
        }
        return tools;
    }
}
