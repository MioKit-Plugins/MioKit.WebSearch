using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WebSearch.Services;

public sealed class WebsiteStorageService
{
    private readonly ConditionalWeakTable<WebsiteNode, SemaphoreSlim> gates = new();
    public async Task<object?> ExecuteAsync(WebsitePageContext context, string operation, string key, string json)
    {
        context.Check("storage");
        var gate = gates.GetValue(context.Node, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            context.Check("storage");
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                await context.Node.GetWebsiteStorageJsonAsync() ?? "{}") ?? [];
            object? result = null;
            switch (operation)
            {
                case "get": result = values.TryGetValue(key, out var value) ? value : null; break;
                case "keys": result = values.Keys.ToArray(); break;
                case "set": values[key] = JsonSerializer.Deserialize<JsonElement>(json); break;
                case "remove": values.Remove(key); break;
                case "clear": values.Clear(); break;
                default: throw new WebsiteAccessException("operation.invalid", "未知存储操作。");
            }
            if (operation is "set" or "remove" or "clear")
            {
                context.Check("storage");
                context.Node.SetWebsiteStorageJson(JsonSerializer.Serialize(values));
            }
            return result;
        }
        finally { gate.Release(); }
    }
}

