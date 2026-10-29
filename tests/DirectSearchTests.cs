using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class DirectSearchTests
{
    [Theory]
    [InlineData("%", "\\%")]
    [InlineData("_", "\\_")]
    [InlineData("\\", "\\\\")]
    [InlineData("100%_done", "100\\%\\_done")]
    public void SqlLikeWildcardsAreEscaped(string input, string expected)
    {
        Assert.Equal(expected, BrowserIndexService.EscapeLike(input));
    }
}
