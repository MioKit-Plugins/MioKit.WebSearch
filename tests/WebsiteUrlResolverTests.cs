using WebSearch.Models;
using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class WebsiteUrlResolverTests
{
    [Fact]
    public void ReplacesEveryQueryTokenWithUrlEncodedInput()
    {
        var result = WebsiteUrlResolver.Resolve(
            "https://example.test/{query}?again={QUERY}",
            "中文 test",
            "clipboard",
            QuerySourcePolicy.InputOnly);

        Assert.Equal(
            "https://example.test/%E4%B8%AD%E6%96%87%20test?again=%E4%B8%AD%E6%96%87%20test",
            result);
    }

    [Fact]
    public void InputThenClipboardUsesClipboardWhenInputIsBlank()
    {
        var result = WebsiteUrlResolver.Resolve(
            "https://example.test/?q={query}",
            " ",
            "copied text",
            QuerySourcePolicy.InputThenClipboard);

        Assert.Equal("https://example.test/?q=copied%20text", result);
    }

    [Fact]
    public void ClipboardOnlyIgnoresTypedInput()
    {
        var result = WebsiteUrlResolver.Resolve(
            "https://example.test/?q={query}",
            "typed",
            "copied",
            QuerySourcePolicy.ClipboardOnly);

        Assert.Equal("https://example.test/?q=copied", result);
    }

    [Fact]
    public void HotKeyUsesClipboardTextAsQuery()
    {
        var result = WebsiteUrlResolver.ResolveForHotKey(
            "https://example.test/?q={query}",
            "selected text");

        Assert.Equal("https://example.test/?q=selected%20text", result);
    }

    [Fact]
    public void HotKeyUsesEmptyQueryWhenClipboardIsBlank()
    {
        var result = WebsiteUrlResolver.ResolveForHotKey(
            "https://example.test/?q={query}",
            "  ");

        Assert.Equal("https://example.test/?q=", result);
    }

    [Theory]
    [InlineData("https://example.test")]
    [InlineData("http://example.test/search?q={query}")]
    public void AcceptsOnlyHttpTemplates(string value)
    {
        Assert.True(WebsiteUrlResolver.TryValidateHttpUrl(value, out _));
    }

    [Theory]
    [InlineData("file:///c:/temp")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void RejectsUnsafeTemplates(string value)
    {
        Assert.False(WebsiteUrlResolver.TryValidateHttpUrl(value, out _));
    }
}
