using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// Projects a server-authenticated session into action-level UI capabilities.
/// Every action is bound to the same named policy selected by its backend endpoint.
/// </summary>
public sealed class AuthenticatedContextProjectionService
{
    private static readonly CapabilityPolicyBinding[] CapabilityBindings =
    [
        new(ClearVisionCapabilities.ProjectEdit, ClearVisionAuthorizationPolicies.CanEditProject),
        new(ClearVisionCapabilities.InspectionResultsRead, ClearVisionAuthorizationPolicies.CanReadInspectionResults),

        new(ClearVisionCapabilities.StationSensitiveRead, ClearVisionAuthorizationPolicies.RequireStationAdmin),
        new(ClearVisionCapabilities.StationCommandsCreate, ClearVisionAuthorizationPolicies.RequireStationAdmin),
        new(ClearVisionCapabilities.StationPackagesRead, ClearVisionAuthorizationPolicies.RequireStationAdmin),
        new(ClearVisionCapabilities.StationPackagesDeploy, ClearVisionAuthorizationPolicies.RequireStationAdmin),
        new(ClearVisionCapabilities.StationTestPackagesCreate, ClearVisionAuthorizationPolicies.RequireStationAdmin),

        new(ClearVisionCapabilities.SettingsUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.SettingsReset, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.PlcSettingsUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.PlcMappingsUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.PlcConnectionTest, ClearVisionAuthorizationPolicies.CanOperateHardware),
        new(ClearVisionCapabilities.TcpProfilesUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.TcpConnectionsOperate, ClearVisionAuthorizationPolicies.CanOperateHardware),
        new(ClearVisionCapabilities.StationCommunicationUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.StationCommunicationTokenManage, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.CameraBindingsUpdate, ClearVisionAuthorizationPolicies.CanOperateHardware),
        new(ClearVisionCapabilities.CameraCapture, ClearVisionAuthorizationPolicies.CanOperateHardware),
        new(ClearVisionCapabilities.CameraPreviewOperate, ClearVisionAuthorizationPolicies.CanOperateHardware),
        new(ClearVisionCapabilities.TriggerInputOperate, ClearVisionAuthorizationPolicies.CanOperateHardware),

        new(ClearVisionCapabilities.AiModelsCreate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.AiModelsUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.AiModelsDelete, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.AiModelsActivate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.AiModelsSetDefault, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.AiModelsTest, ClearVisionAuthorizationPolicies.RequireAdmin),

        new(ClearVisionCapabilities.DatabaseStatusRead, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.DatabaseBackup, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.DatabaseRepair, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.DatabaseRestore, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.DatabaseCleanup, ClearVisionAuthorizationPolicies.RequireAdmin),

        new(ClearVisionCapabilities.UsersRead, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.UsersCreate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.UsersUpdate, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.UsersDelete, ClearVisionAuthorizationPolicies.RequireAdmin),
        new(ClearVisionCapabilities.UsersResetPassword, ClearVisionAuthorizationPolicies.RequireAdmin)
    ];

    private readonly IConfigurationService _configurationService;

    public AuthenticatedContextProjectionService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public AuthenticatedContextResponse Project(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var capabilities = CapabilityBindings
            .Where(binding => ClearVisionAuthorizationPolicies.IsAllowed(session.Role, binding.Policy))
            .Select(binding => binding.Capability)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new AuthenticatedContextResponse
        {
            UserId = session.UserId,
            Username = session.Username,
            Role = session.Role,
            Capabilities = capabilities,
            PasswordPolicy = new AuthenticatedPasswordPolicyResponse
            {
                MinimumLength = Math.Max(6, _configurationService.GetCurrent()?.Security?.PasswordMinLength ?? 6)
            }
        };
    }

    internal sealed record CapabilityPolicyBinding(string Capability, string Policy);
}
