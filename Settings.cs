namespace KeyTranslate;

internal sealed class Settings
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Hotkey { get; set; } = "F8";
    public bool AutoTranslate { get; set; }
    public bool RunAtStartup { get; set; }
    public int DelaySeconds { get; set; } = 2;
}

