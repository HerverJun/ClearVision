namespace ClearVision.Product.Desktop.Configuration;

public sealed class StudioOptions
{
    public const string SectionName = "Studio";

    public bool WorkspaceV2Enabled { get; set; }
    public bool NodePreviewInspectorEnabled { get; set; }
    public bool CircleSearchV2ToolEnabled { get; set; } = true;
}
