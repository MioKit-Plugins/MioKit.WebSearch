using Microsoft.Web.WebView2.Core;

namespace WebSearch.Services;

internal sealed record WebsiteWebMessage(string Source, string Json);

/// <summary>Browser I/O boundary, allowing session security tests without creating any UI.</summary>
internal interface IWebsiteMessageChannel : IDisposable
{
    string Source { get; }
    event EventHandler<WebsiteWebMessage>? MessageReceived;
    void Send(string message);
    Task ExecuteScriptAsync(string script);
}
internal sealed class CoreWebsiteMessageChannel : IWebsiteMessageChannel
{
    private readonly CoreWebView2 core;
    public string Source => core.Source;
    public event EventHandler<WebsiteWebMessage>? MessageReceived;
    public CoreWebsiteMessageChannel(CoreWebView2 core)
    {
        this.core = core;
        core.WebMessageReceived += Receive;
    }
    private void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch { json = ""; }
        MessageReceived?.Invoke(this, new(e.Source, json));
    }
    public void Send(string message) => core.PostWebMessageAsString(message);
    public async Task ExecuteScriptAsync(string script) => await core.ExecuteScriptAsync(script);
    public void Dispose() => core.WebMessageReceived -= Receive;
}

