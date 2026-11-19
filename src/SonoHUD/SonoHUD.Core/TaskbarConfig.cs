namespace SonoHUD.Core;

public enum TaskbarPosition { Start, End }
public enum TaskbarStyle { Subtle, Solid }

// Independent of the single desktop overlay. Missing settings in schema 2 stay OFF.
public sealed record TaskbarConfig
{
    public bool Enabled { get; set; }
    public string MonitorId { get; set; } = "";
    public TaskbarPosition Position { get; set; } = TaskbarPosition.Start;
    public bool ShowTitle { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowArtwork { get; set; } = true;
    public bool ShowControls { get; set; } = true;
    public bool ControlsLeft { get; set; }
    public bool AutoWidth { get; set; } = true;
    public double Width { get; set; } = 320;
    public bool ScrollTitle { get; set; } = true;
    public bool Compact { get; set; }
    public TaskbarStyle Style { get; set; } = TaskbarStyle.Subtle;
}
