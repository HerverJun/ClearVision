using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Application.Security;

/// <summary>
/// Stable names and role rules used by both endpoint authorization and authenticated capability projection.
/// Endpoint code remains responsible for selecting the policy that protects each route.
/// </summary>
public static class ClearVisionAuthorizationPolicies
{
    public const string RequireAuthenticated = nameof(RequireAuthenticated);
    public const string RequireAdmin = nameof(RequireAdmin);
    public const string RequireEngineerOrAdmin = nameof(RequireEngineerOrAdmin);
    public const string RequireStationAdmin = nameof(RequireStationAdmin);
    public const string CanEditProject = nameof(CanEditProject);
    public const string CanOperateHardware = nameof(CanOperateHardware);
    public const string CanReadSensitiveConfig = nameof(CanReadSensitiveConfig);
    public const string CanReadInspectionResults = nameof(CanReadInspectionResults);

    public static bool IsAllowed(string? role, string policy)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        return policy switch
        {
            RequireAuthenticated => IsKnownRole(role),
            RequireAdmin => IsAdmin(role),
            RequireEngineerOrAdmin => IsEngineerOrAdmin(role),
            RequireStationAdmin => IsAdmin(role),
            CanEditProject => IsEngineerOrAdmin(role),
            CanOperateHardware => IsEngineerOrAdmin(role),
            CanReadSensitiveConfig => IsAdmin(role),
            CanReadInspectionResults => IsKnownRole(role),
            _ => false
        };
    }

    private static bool IsKnownRole(string role) =>
        IsEngineerOrAdmin(role) ||
        string.Equals(role, UserRole.Operator.ToString(), StringComparison.Ordinal);

    private static bool IsAdmin(string role) =>
        string.Equals(role, UserRole.Admin.ToString(), StringComparison.Ordinal);

    private static bool IsEngineerOrAdmin(string role) =>
        IsAdmin(role) ||
        string.Equals(role, UserRole.Engineer.ToString(), StringComparison.Ordinal);
}

/// <summary>
/// Action-level capability names consumed by the production web UI.
/// </summary>
public static class ClearVisionCapabilities
{
    public const string ProjectEdit = "project.edit";
    public const string InspectionResultsRead = "inspection.results.read";

    public const string StationSensitiveRead = "station.sensitive.read";
    public const string StationCommandsCreate = "station.commands.create";
    public const string StationPackagesRead = "station.packages.read";
    public const string StationPackagesDeploy = "station.packages.deploy";
    public const string StationTestPackagesCreate = "station.test-packages.create";

    public const string SettingsUpdate = "settings.update";
    public const string SettingsReset = "settings.reset";
    public const string PlcSettingsUpdate = "plc.settings.update";
    public const string PlcMappingsUpdate = "plc.mappings.update";
    public const string PlcConnectionTest = "plc.connection.test";
    public const string TcpProfilesUpdate = "tcp.profiles.update";
    public const string TcpConnectionsOperate = "tcp.connections.operate";
    public const string StationCommunicationUpdate = "station.communication.update";
    public const string StationCommunicationTokenManage = "station.communication-token.manage";
    public const string CameraBindingsUpdate = "cameras.bindings.update";
    public const string CameraCapture = "cameras.capture";
    public const string CameraPreviewOperate = "cameras.preview.operate";
    public const string TriggerInputOperate = "trigger-input.operate";

    public const string AiModelsCreate = "ai.models.create";
    public const string AiModelsUpdate = "ai.models.update";
    public const string AiModelsDelete = "ai.models.delete";
    public const string AiModelsActivate = "ai.models.activate";
    public const string AiModelsSetDefault = "ai.models.set-default";
    public const string AiModelsTest = "ai.models.test";

    public const string DatabaseStatusRead = "database.status.read";
    public const string DatabaseBackup = "database.backup";
    public const string DatabaseRepair = "database.repair";
    public const string DatabaseRestore = "database.restore";
    public const string DatabaseCleanup = "database.cleanup";

    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";
    public const string UsersResetPassword = "users.reset-password";
}
