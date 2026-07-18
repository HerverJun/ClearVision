namespace ClearVision.Product.Desktop.Configuration;

public sealed class StudioOptions
{
    public const string SectionName = "Studio";

    public bool StudioUiEnabled { get; set; } = false;
    public bool WorkspaceCapabilityEnabled { get; set; } = false;
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

internal static class StudioStartupProfileCatalog
{
    public const string LegacyDefault = "LEGACY_DEFAULT";
    public const string NextPilot = "NEXT_PILOT";
    public const string NextFullCandidate = "NEXT_FULL_CANDIDATE";
    public const string IsolatedTruthTable = "ISOLATED_TRUTH_TABLE";

    public static string Resolve(
        StudioOptions options,
        string? requestedProfile = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(requestedProfile))
        {
            if (!options.StudioUiEnabled && !options.WorkspaceCapabilityEnabled)
            {
                return LegacyDefault;
            }

            if (options.StudioUiEnabled && options.WorkspaceCapabilityEnabled)
            {
                return NextFullCandidate;
            }

            return IsolatedTruthTable;
        }

        var normalized = requestedProfile.Trim().ToUpperInvariant();
        var matches = normalized switch
        {
            LegacyDefault => !options.StudioUiEnabled && !options.WorkspaceCapabilityEnabled,
            NextPilot or NextFullCandidate =>
                options.StudioUiEnabled && options.WorkspaceCapabilityEnabled,
            _ => throw new InvalidOperationException(
                $"Unknown Studio startup profile '{requestedProfile}'.")
        };

        if (!matches)
        {
            throw new InvalidOperationException(
                $"Studio startup profile '{normalized}' does not match the configured root/workspace flags.");
        }

        return normalized;
    }
}
