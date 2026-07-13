namespace ClearVision.Product.Desktop.Configuration;

public sealed class StudioOptions
{
    public const string SectionName = "Studio";

    public bool StudioUiEnabled { get; set; } = false;
    public bool NodePreviewInspectorEnabled { get; set; }
    public bool PropertyPanelCapabilityEnabled { get; set; }
    public bool PreviewPanelCapabilityEnabled { get; set; }
    public bool GlobalVariablesCapabilityEnabled { get; set; }
    public bool SettingsCapabilityEnabled { get; set; }
    public bool ProjectPageCapabilityEnabled { get; set; }
    public bool InspectionCapabilityEnabled { get; set; }
    public bool ResultsReviewCapabilityEnabled { get; set; }
    public bool AiPanelCapabilityEnabled { get; set; }
    public bool CircleSearchV2ToolEnabled { get; set; } = true;
    public bool NPointCalibrationWorkbenchEnabled { get; set; } = true;
}
