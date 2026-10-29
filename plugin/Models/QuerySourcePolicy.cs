namespace WebSearch.Models;

public enum QuerySourcePolicy
{
    InputOnly,
    ClipboardOnly,
    InputThenClipboard
}

public static class BrowserIds
{
    public const string SystemDefault = "system";
    public const string Edge = "edge";
    public const string Chrome = "chrome";
    public const string Firefox = "firefox";
}

