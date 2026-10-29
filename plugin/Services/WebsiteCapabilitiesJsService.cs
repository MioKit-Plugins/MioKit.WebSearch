using System.Diagnostics;
using System.Text.Json;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;

namespace WebSearch.Services;

[JsService("WebsiteCapabilities")]
public sealed class WebsiteCapabilitiesJsService(
    WebsitePageContext context, WebsiteStorageService storage, IToastService toasts)
{
    // One bounded service surface, with a stable result envelope for arbitrary web frameworks.
    public async Task<MioApiResult> Invoke(string service, string operation, string argumentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentJson);
            var args = document.RootElement;
            string Text(string key) => args.TryGetProperty(key, out var x) ? x.GetString() ?? "" : "";
            object? value = null;
            switch (service)
            {
                case "storage":
                    value = await storage.ExecuteAsync(context, operation, Text("key"),
                        args.TryGetProperty("value", out var v) ? v.GetRawText() : "null");
                    break;
                case "files":
                {
                    var write = operation is "writeText" or "writeBase64" or "createDirectory" or "move" or "delete";
                    var path = context.AuthorizePath(Text("path"), write);
                    switch (operation)
                    {
                        case "readText": value = await Task.Run(() => File.ReadAllText(path)); break;
                        case "readBase64": value = await Task.Run(() => Convert.ToBase64String(File.ReadAllBytes(path))); break;
                        case "list": value = await Task.Run(() => Directory.EnumerateFileSystemEntries(path).Select(p => new { path = p, name = Path.GetFileName(p), isDirectory = Directory.Exists(p) }).ToArray()); break;
                        case "writeText": await Task.Run(() => { context.AuthorizePath(path, true); File.WriteAllText(path, Text("content")); }); break;
                        case "writeBase64": await Task.Run(() => { context.AuthorizePath(path, true); File.WriteAllBytes(path, Convert.FromBase64String(Text("content"))); }); break;
                        case "createDirectory": await Task.Run(() => { context.AuthorizePath(path, true); Directory.CreateDirectory(path); }); break;
                        case "delete": await Task.Run(() => { context.AuthorizePath(path, true); File.Delete(path); }); break;
                        case "move":
                            var target = context.AuthorizePath(Text("target"), true);
                            await Task.Run(() => { context.AuthorizePath(path, true); context.AuthorizePath(target, true); File.Move(path, target); });
                            break;
                        default: throw new WebsiteAccessException("operation.invalid", "未知文件操作。");
                    }
                    break;
                }
                case "dialogs":
                {
                    if (operation == "openFile")
                    {
                        context.Check("files.read");
                        var files = await context.Owner.StorageProvider.OpenFilePickerAsync(new() { Title = "选择文件", AllowMultiple = false });
                        context.Check("files.read");
                        var path = files.FirstOrDefault()?.TryGetLocalPath();
                        if (path is not null) context.Grant(path, false, false);
                        value = path;
                    }
                    else if (operation == "saveFile")
                    {
                        context.Check("files.write");
                        var file = await context.Owner.StorageProvider.SaveFilePickerAsync(new() { Title = "保存文件", SuggestedFileName = Text("name") });
                        context.Check("files.write");
                        var path = file?.TryGetLocalPath();
                        if (path is not null) context.Grant(path, false, true);
                        value = path;
                    }
                    else if (operation == "openDirectory")
                    {
                        var write = args.TryGetProperty("write", out var w) && w.GetBoolean();
                        var permission = write ? "files.write" : "files.read";
                        context.Check(permission);
                        var folders = await context.Owner.StorageProvider.OpenFolderPickerAsync(new() { Title = write ? "授权读写所选目录" : "授权读取所选目录", AllowMultiple = false });
                        context.Check(permission);
                        var path = folders.FirstOrDefault()?.TryGetLocalPath();
                        if (path is not null) context.Grant(path, true, write);
                        value = path;
                    }
                    else throw new WebsiteAccessException("operation.invalid", "未知选择器操作。");
                    break;
                }
                case "clipboard":
                    if (operation is not ("readText" or "writeText"))
                        throw new WebsiteAccessException("operation.invalid", "未知剪贴板操作。");
                    context.Check(operation == "readText" ? "clipboard.read" : "clipboard.write");
                    var clipboard = context.Owner.Clipboard ?? throw new InvalidOperationException("剪贴板不可用。");
                    if (operation == "readText") value = await clipboard.TryGetTextAsync();
                    else if (operation == "writeText") await clipboard.SetTextAsync(Text("text"));
                    else throw new WebsiteAccessException("operation.invalid", "未知剪贴板操作。");
                    break;
                case "notifications":
                    if (operation != "show") throw new WebsiteAccessException("operation.invalid", "未知通知操作。");
                    context.Check("notifications");
                    value = toasts.Show(Text("message"), ToastLevel.Info, Text("title")).ToString();
                    break;
                case "external":
                    if (operation != "open") throw new WebsiteAccessException("operation.invalid", "未知外部打开操作。");
                    context.EnsureDocument();
                    if (!WebsiteUrlResolver.TryValidateHttpUrl(Text("url"), out var uri))
                        throw new WebsiteAccessException("url.invalid", "仅允许网页链接。");
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    break;
                default: throw new WebsiteAccessException("service.invalid", "未知服务。");
            }
            return new(true, value, null, null);
        }
        catch (WebsiteAccessException ex) { return new(false, null, ex.Code, ex.Message); }
        catch (Exception ex) { return new(false, null, "operation.failed", ex.Message); }
    }
}

