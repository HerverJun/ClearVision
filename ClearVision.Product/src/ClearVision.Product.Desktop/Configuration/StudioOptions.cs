namespace ClearVision.Product.Desktop.Configuration;

public sealed class StudioOptions
{
    public const string SectionName = "Studio";

    // A named startup profile selects the mounted UI root at the next process start.
    // It only projects UI visibility. The raw capability values and backend
    // authorization remain the service authority.
    public string? StartupProfile { get; set; }
    public bool StudioUiEnabled { get; set; } = false;
    public bool WorkspaceCapabilityEnabled { get; set; } = false;
    public bool NodePreviewInspectorEnabled { get; set; }
    public bool PropertyPanelCapabilityEnabled { get; set; }
    public bool PreviewPanelCapabilityEnabled { get; set; }
    public bool GlobalVariablesCapabilityEnabled { get; set; }
    public bool SettingsCapabilityEnabled { get; set; }
    public bool ProjectPageCapabilityEnabled { get; set; }
    public bool InspectionCapabilityEnabled { get; set; }
    public bool StationsReadCapabilityEnabled { get; set; } = false;
    public bool InspectionRunCapabilityEnabled { get; set; } = false;
    public bool ResultsReviewCapabilityEnabled { get; set; }
    public bool AiPanelCapabilityEnabled { get; set; }
    public bool AiWorkbenchCapabilityEnabled { get; set; } = false;
    public bool CircleSearchV2ToolEnabled { get; set; } = true;
    public bool NPointCalibrationWorkbenchEnabled { get; set; } = true;
}

internal static class StudioStartupProfileCatalog
{
    public const string LegacyDefault = "LEGACY_DEFAULT";
    public const string LegacyFallback = "LEGACY_FALLBACK";
    public const string NextInternalPilot = "NEXT_INTERNAL_PILOT";
    public const string NextEngineerPilot = "NEXT_ENGINEER_PILOT";
    public const string NextOperatorPilot = "NEXT_OPERATOR_PILOT";
    public const string NextDefaultCandidate = "NEXT_DEFAULT_CANDIDATE";
    public const string NextDefault = "NEXT_DEFAULT";

    // Retained for historical evidence harnesses and existing explicit
    // deployments. New configuration should use NEXT_INTERNAL_PILOT instead.
    public const string NextPilot = "NEXT_PILOT";
    public const string NextFullCandidate = "NEXT_FULL_CANDIDATE";
    public const string IsolatedTruthTable = "ISOLATED_TRUTH_TABLE";

    private static readonly IReadOnlyList<string> AdministratorOnlyRoles =
        Array.AsReadOnly(["Admin"]);
    private static readonly IReadOnlyList<string> EditorRoles =
        Array.AsReadOnly(["Admin", "Engineer"]);
    private static readonly IReadOnlyList<string> OperatorOnlyRoles =
        Array.AsReadOnly(["Operator"]);
    private static readonly IReadOnlyList<string> ProductRoles =
        Array.AsReadOnly(["Admin", "Engineer", "Operator"]);

    private static readonly IReadOnlyDictionary<string, StudioStartupProfileDefinition> Definitions =
        new Dictionary<string, StudioStartupProfileDefinition>(StringComparer.Ordinal)
        {
            [LegacyDefault] = new(LegacyDefault, false, false, false, false, false, false, ProductRoles),
            [LegacyFallback] = new(LegacyFallback, false, false, false, false, false, false, ProductRoles),
            [NextInternalPilot] = new(NextInternalPilot, true, true, true, true, true, true, AdministratorOnlyRoles),
            [NextEngineerPilot] = new(NextEngineerPilot, true, true, true, true, true, true, EditorRoles),
            [NextOperatorPilot] = new(NextOperatorPilot, true, false, false, false, false, false, OperatorOnlyRoles),
            [NextDefaultCandidate] = new(NextDefaultCandidate, true, true, true, true, true, true, ProductRoles),
            [NextDefault] = new(NextDefault, true, true, true, true, true, true, ProductRoles),
            [NextPilot] = new(NextPilot, true, true, true, true, true, true, AdministratorOnlyRoles),
            [NextFullCandidate] = new(NextFullCandidate, true, true, true, true, true, true, ProductRoles)
        };

    public static IReadOnlyList<string> AllowedRolesFor(string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        if (string.Equals(
                profile.Trim(),
                IsolatedTruthTable,
                StringComparison.OrdinalIgnoreCase))
        {
            // This compatibility label is generated only from pre-F09 raw
            // options. It is never a valid configured profile, but a
            // StudioUI compatibility start still needs an explicit role
            // projection for the strict startup contract.
            return ProductRoles;
        }

        return DefinitionFor(profile).AllowedRoles;
    }

    public static bool IsKnownConfiguredProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        return Definitions.ContainsKey(profile.Trim().ToUpperInvariant());
    }

    public static string Resolve(
        StudioOptions options,
        string? requestedProfile = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var selectedProfile = string.IsNullOrWhiteSpace(requestedProfile)
            ? options.StartupProfile
            : requestedProfile;
        if (string.IsNullOrWhiteSpace(selectedProfile))
        {
            return ResolveLegacyTruthTable(options);
        }

        return DefinitionFor(selectedProfile).Name;
    }

    public static StudioOptions CreateEffectiveOptions(
        StudioOptions configuredOptions,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(configuredOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        if (string.Equals(
                profile.Trim(),
                IsolatedTruthTable,
                StringComparison.OrdinalIgnoreCase))
        {
            // Preserve the legacy truth-table output exactly. It remains
            // available only for absent-profile compatibility; named F09
            // profiles are always projected from the catalog below.
            return CloneConfiguredOptions(configuredOptions, IsolatedTruthTable);
        }

        var definition = DefinitionFor(profile);
        var preservesLegacyCapabilities = !definition.UsesStudioUi;

        return new StudioOptions
        {
            StartupProfile = definition.Name,
            StudioUiEnabled = definition.UsesStudioUi,
            WorkspaceCapabilityEnabled = definition.WorkspaceCapabilityEnabled,
            NodePreviewInspectorEnabled = configuredOptions.NodePreviewInspectorEnabled,
            PropertyPanelCapabilityEnabled = configuredOptions.PropertyPanelCapabilityEnabled,
            PreviewPanelCapabilityEnabled = configuredOptions.PreviewPanelCapabilityEnabled,
            GlobalVariablesCapabilityEnabled = configuredOptions.GlobalVariablesCapabilityEnabled,
            SettingsCapabilityEnabled = (preservesLegacyCapabilities || definition.AllowsSettings) && configuredOptions.SettingsCapabilityEnabled,
            ProjectPageCapabilityEnabled = configuredOptions.ProjectPageCapabilityEnabled,
            InspectionCapabilityEnabled = configuredOptions.InspectionCapabilityEnabled,
            StationsReadCapabilityEnabled = configuredOptions.StationsReadCapabilityEnabled,
            InspectionRunCapabilityEnabled = (preservesLegacyCapabilities || definition.AllowsInspectionRun) && configuredOptions.InspectionRunCapabilityEnabled,
            ResultsReviewCapabilityEnabled = configuredOptions.ResultsReviewCapabilityEnabled,
            AiPanelCapabilityEnabled = (preservesLegacyCapabilities || definition.AllowsAiPanel) && configuredOptions.AiPanelCapabilityEnabled,
            AiWorkbenchCapabilityEnabled = (preservesLegacyCapabilities || definition.AllowsAiWorkbench) && configuredOptions.AiWorkbenchCapabilityEnabled,
            CircleSearchV2ToolEnabled = configuredOptions.CircleSearchV2ToolEnabled,
            NPointCalibrationWorkbenchEnabled = configuredOptions.NPointCalibrationWorkbenchEnabled
        };
    }

    private static StudioOptions CloneConfiguredOptions(
        StudioOptions configuredOptions,
        string profile)
    {
        return new StudioOptions
        {
            StartupProfile = profile,
            StudioUiEnabled = configuredOptions.StudioUiEnabled,
            WorkspaceCapabilityEnabled = configuredOptions.WorkspaceCapabilityEnabled,
            NodePreviewInspectorEnabled = configuredOptions.NodePreviewInspectorEnabled,
            PropertyPanelCapabilityEnabled = configuredOptions.PropertyPanelCapabilityEnabled,
            PreviewPanelCapabilityEnabled = configuredOptions.PreviewPanelCapabilityEnabled,
            GlobalVariablesCapabilityEnabled = configuredOptions.GlobalVariablesCapabilityEnabled,
            SettingsCapabilityEnabled = configuredOptions.SettingsCapabilityEnabled,
            ProjectPageCapabilityEnabled = configuredOptions.ProjectPageCapabilityEnabled,
            InspectionCapabilityEnabled = configuredOptions.InspectionCapabilityEnabled,
            StationsReadCapabilityEnabled = configuredOptions.StationsReadCapabilityEnabled,
            InspectionRunCapabilityEnabled = configuredOptions.InspectionRunCapabilityEnabled,
            ResultsReviewCapabilityEnabled = configuredOptions.ResultsReviewCapabilityEnabled,
            AiPanelCapabilityEnabled = configuredOptions.AiPanelCapabilityEnabled,
            AiWorkbenchCapabilityEnabled = configuredOptions.AiWorkbenchCapabilityEnabled,
            CircleSearchV2ToolEnabled = configuredOptions.CircleSearchV2ToolEnabled,
            NPointCalibrationWorkbenchEnabled = configuredOptions.NPointCalibrationWorkbenchEnabled
        };
    }

    private static string ResolveLegacyTruthTable(StudioOptions options)
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

    private static StudioStartupProfileDefinition DefinitionFor(string profile)
    {
        var normalized = profile.Trim().ToUpperInvariant();
        if (Definitions.TryGetValue(normalized, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Unknown Studio startup profile '{profile}'.");
    }

    private sealed record StudioStartupProfileDefinition(
        string Name,
        bool UsesStudioUi,
        bool WorkspaceCapabilityEnabled,
        bool AllowsSettings,
        bool AllowsInspectionRun,
        bool AllowsAiPanel,
        bool AllowsAiWorkbench,
        IReadOnlyList<string> AllowedRoles);
}
