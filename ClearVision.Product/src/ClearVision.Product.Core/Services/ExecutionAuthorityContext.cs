using System.Collections.ObjectModel;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Authenticated identity captured at the execution boundary.  System identities
/// are reserved for server-created stored-project and runtime-package work.
/// </summary>
public sealed record ExecutionPrincipal(
    string SubjectId,
    string Name,
    string Role,
    bool IsAuthenticated,
    bool IsSystem = false)
{
    public static ExecutionPrincipal System(string name = "ClearVision.Runtime") =>
        new("system", name, "System", true, true);

    public bool IsOperator => string.Equals(Role, "Operator", StringComparison.OrdinalIgnoreCase);
    public bool IsEngineerOrAdmin =>
        string.Equals(Role, "Engineer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Exact capability declaration supplied for a draft execution.  The server
/// compares it with the capabilities derived from the immutable flow.
/// </summary>
public sealed class ExecutionCapabilityManifest
{
    public ExecutionCapabilityManifest(ExecutionSideEffect capabilities, bool isExplicit)
    {
        Capabilities = capabilities;
        IsExplicit = isExplicit;
    }

    public ExecutionSideEffect Capabilities { get; }
    public bool IsExplicit { get; }

    public static ExecutionCapabilityManifest Derive(OperatorFlow flow, bool isExplicit = false)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var capabilities = NestedExecutionFlowCatalog.EnumerateEnabledOperators(flow)
            .Aggregate(
                ExecutionSideEffect.None,
                (current, item) => current | ExecutionSideEffectCatalog.GetCapabilities(item));
        return new ExecutionCapabilityManifest(capabilities, isExplicit);
    }
}

/// <summary>
/// Authority evidence passed by an authenticated user-facing execution surface.
/// ResourceBindings contains opaque server profile/binding identifiers only;
/// endpoints never copy raw paths, URLs, connection strings, or device targets.
/// </summary>
public sealed class ExecutionRequestAuthority
{
    public ExecutionRequestAuthority(
        ExecutionPrincipal principal,
        long? expectedProjectRevision = null,
        ExecutionCapabilityManifest? capabilityManifest = null,
        string? confirmationId = null,
        string? auditId = null,
        IReadOnlyDictionary<string, string>? resourceBindings = null,
        bool isInternalSystemRequest = false)
    {
        Principal = principal ?? throw new ArgumentNullException(nameof(principal));
        ExpectedProjectRevision = expectedProjectRevision;
        CapabilityManifest = capabilityManifest;
        ConfirmationId = Normalize(confirmationId);
        AuditId = Normalize(auditId);
        ResourceBindings = new ReadOnlyDictionary<string, string>(
            (resourceBindings ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value.Trim(),
                    StringComparer.Ordinal));
        IsInternalSystemRequest = isInternalSystemRequest;
    }

    public ExecutionPrincipal Principal { get; }
    public long? ExpectedProjectRevision { get; }
    public ExecutionCapabilityManifest? CapabilityManifest { get; }
    public string? ConfirmationId { get; }
    public string? AuditId { get; }
    public IReadOnlyDictionary<string, string> ResourceBindings { get; }
    public bool IsInternalSystemRequest { get; }

    public static ExecutionRequestAuthority InternalSystem { get; } = new(
        ExecutionPrincipal.System(),
        isInternalSystemRequest: true);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Composite identity for stateful operators.  A caller-controlled operator id
/// alone can never address production state.
/// </summary>
public readonly record struct ExecutionStateKey(
    Guid ProjectId,
    Guid SessionId,
    Guid FlowId,
    Guid RunId,
    Guid OperatorId,
    ExecutionSnapshotSource Source)
{
    public static ExecutionStateKey ForOperator(Guid operatorId)
    {
        var scope = ExecutionAuthorityContext.Current;
        if (scope == null)
        {
            throw new InvalidOperationException(
                "EXECUTION_STATE_AUTHORITY_REQUIRED: Stateful operators require a governed execution scope.");
        }

        return new ExecutionStateKey(
            scope.ProjectId,
            scope.SessionId,
            scope.FlowId,
            scope.RunId,
            operatorId,
            scope.Source);
    }
}

public sealed record ExecutionAuthorityScope(
    Guid SnapshotId,
    Guid ProjectId,
    Guid SessionId,
    Guid FlowId,
    Guid RunId,
    ExecutionSnapshotSource Source,
    ExecutionRunMode RunMode,
    string FlowHash,
    ExecutionPrincipal Principal,
    IReadOnlyDictionary<string, string> ResourceBindings)
{
    /// <summary>
    /// The immutable snapshot that installed this scope. Manually-created
    /// state-only scopes deliberately have no nested-execution authority.
    /// </summary>
    public ExecutionSnapshot? CapturedSnapshot { get; internal init; }
}

/// <summary>
/// Async-flow-local execution identity installed only by the governed adapter.
/// It is restored on nested execution and cannot leak between concurrent runs.
/// </summary>
public static class ExecutionAuthorityContext
{
    private static readonly AsyncLocal<ExecutionAuthorityScope?> CurrentScope = new();

    public static ExecutionAuthorityScope? Current => CurrentScope.Value;

    public static IDisposable Enter(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Enter(new ExecutionAuthorityScope(
            snapshot.SnapshotId,
            snapshot.ProjectId,
            snapshot.SessionId,
            snapshot.FlowId,
            snapshot.RunId,
            snapshot.Source,
            snapshot.RunMode,
            snapshot.FlowHash,
            snapshot.Principal,
            snapshot.ResourceBindings)
        {
            CapturedSnapshot = snapshot
        });
    }

    public static IDisposable Enter(ExecutionAuthorityScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = CurrentScope.Value;
        CurrentScope.Value = scope;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(ExecutionAuthorityScope? previous) : IDisposable
    {
        private ExecutionAuthorityScope? _previous = previous;

        public void Dispose()
        {
            CurrentScope.Value = Interlocked.Exchange(ref _previous, null);
        }
    }
}

public sealed record ExecutionAuthorityDecision(bool Allowed, string Code, string Message)
{
    public static ExecutionAuthorityDecision Allow() => new(true, "EXECUTION_AUTHORITY_ALLOWED", string.Empty);
    public static ExecutionAuthorityDecision Reject(string code, string message) => new(false, code, message);
}

/// <summary>
/// Source x mode x principal x capability authority matrix.  This check is
/// deliberately independent of HTTP endpoints and is repeated by the governed
/// adapter immediately before graph validation/dispatch.
/// </summary>
public static class ExecutionAuthorityMatrix
{
    private const ExecutionSideEffect DraftExternalEffects =
        ExecutionSideEffect.DeviceRead |
        ExecutionSideEffect.FileRead |
        ExecutionSideEffect.FileWrite |
        ExecutionSideEffect.NetworkWrite |
        ExecutionSideEffect.DeviceWrite;

    public static ExecutionAuthorityDecision Validate(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Principal.IsAuthenticated || string.IsNullOrWhiteSpace(snapshot.Principal.SubjectId))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_PRINCIPAL_REQUIRED",
                "Execution requires an authenticated principal.");
        }

        ExecutionSideEffect flowCapabilities;
        try
        {
            flowCapabilities = ExecutionCapabilityManifest.Derive(snapshot.CreateExecutionFlow()).Capabilities;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("ADMISSION_NESTED_FLOW", StringComparison.Ordinal))
        {
            var separator = ex.Message.IndexOf(':', StringComparison.Ordinal);
            var code = separator > 0 ? ex.Message[..separator] : "ADMISSION_NESTED_FLOW_INVALID";
            var message = separator > 0 ? ex.Message[(separator + 1)..].Trim() : ex.Message;
            return ExecutionAuthorityDecision.Reject(code, message);
        }
        var requiredCapabilities = flowCapabilities | snapshot.ExternalCapabilities;
        if (snapshot.CapabilityManifest.Capabilities != requiredCapabilities)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_CAPABILITY_MANIFEST_MISMATCH",
                "The declared capability manifest does not match the immutable flow.");
        }

        if (snapshot.ExternalCapabilities.HasFlag(ExecutionSideEffect.DeviceRead) &&
            (!snapshot.ResourceBindings.TryGetValue("CameraBindingId", out var cameraBindingId) ||
             string.IsNullOrWhiteSpace(cameraBindingId) ||
             !snapshot.ResourceBindings.TryGetValue("ExternalResource:Camera", out var cameraEvidence) ||
             string.IsNullOrWhiteSpace(cameraEvidence)))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_EXTERNAL_CAMERA_BINDING_REQUIRED",
                "External DeviceRead authority requires a server-issued camera binding manifest.");
        }

        if (snapshot.Principal.IsOperator &&
            snapshot.Source is not (ExecutionSnapshotSource.PersistedProject or ExecutionSnapshotSource.RuntimePackage))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_OPERATOR_AUTHORITATIVE_SOURCE_REQUIRED",
                "Operators may execute only an authoritative stored project or runtime package.");
        }

        return snapshot.Source switch
        {
            ExecutionSnapshotSource.PersistedProject => ValidatePersistedProject(snapshot),
            ExecutionSnapshotSource.RuntimePackage => ValidateRuntimePackage(snapshot),
            ExecutionSnapshotSource.Draft => ValidateDraft(snapshot, flowCapabilities, requiredCapabilities),
            ExecutionSnapshotSource.ShadowBaseline =>
                snapshot.RunMode == ExecutionRunMode.FormalPrimary
                    ? ExecutionAuthorityDecision.Allow()
                    : ExecutionAuthorityDecision.Reject(
                        "ADMISSION_SOURCE_MODE_MISMATCH",
                        "A shadow baseline must use the captured formal-primary mode."),
            ExecutionSnapshotSource.ShadowCandidate =>
                snapshot.RunMode == ExecutionRunMode.ShadowCandidate
                    ? ExecutionAuthorityDecision.Allow()
                    : ExecutionAuthorityDecision.Reject(
                        "ADMISSION_SOURCE_MODE_MISMATCH",
                        "A shadow candidate must use ShadowCandidate mode."),
            _ => ExecutionAuthorityDecision.Reject(
                "ADMISSION_SOURCE_UNKNOWN",
                "The execution snapshot source is not recognized.")
        };
    }

    private static ExecutionAuthorityDecision ValidatePersistedProject(ExecutionSnapshot snapshot)
    {
        if (snapshot.RunMode != ExecutionRunMode.FormalPrimary)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_SOURCE_MODE_MISMATCH",
                "A stored project must execute in FormalPrimary mode.");
        }

        if (!snapshot.ResourceBindings.TryGetValue("ProjectRevision", out var boundRevision) ||
            !long.TryParse(boundRevision, out var parsedRevision) ||
            parsedRevision != snapshot.PersistenceRevision)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_PROJECT_REVISION_BINDING_INVALID",
                "The stored project snapshot is not bound to its persistence revision.");
        }

        return ExecutionAuthorityDecision.Allow();
    }

    private static ExecutionAuthorityDecision ValidateRuntimePackage(ExecutionSnapshot snapshot)
    {
        if (snapshot.RunMode != ExecutionRunMode.StationRuntime ||
            string.IsNullOrWhiteSpace(snapshot.RuntimePackageId) ||
            !snapshot.ResourceBindings.TryGetValue("PackageRoot", out var packageRoot) ||
            string.IsNullOrWhiteSpace(packageRoot) ||
            !snapshot.ResourceBindings.TryGetValue("PackageId", out var packageId) ||
            !string.Equals(packageId, snapshot.RuntimePackageId, StringComparison.Ordinal) ||
            !snapshot.ResourceBindings.TryGetValue("PackageRevision", out var packageRevision) ||
            !long.TryParse(packageRevision, out var parsedRevision) ||
            parsedRevision != snapshot.PersistenceRevision ||
            !snapshot.ResourceBindings.TryGetValue("PackageFlowHash", out var packageFlowHash) ||
            !string.Equals(packageFlowHash, snapshot.FlowHash, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_RUNTIME_PACKAGE_BINDING_INVALID",
                "Station execution requires matching package id, revision, flow hash, and deployment root bindings.");
        }

        return ExecutionAuthorityDecision.Allow();
    }

    private static ExecutionAuthorityDecision ValidateDraft(
        ExecutionSnapshot snapshot,
        ExecutionSideEffect flowCapabilities,
        ExecutionSideEffect requiredCapabilities)
    {
        if (!snapshot.Principal.IsEngineerOrAdmin || snapshot.Principal.IsSystem)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_ROLE_FORBIDDEN",
                "Draft execution requires an authenticated Engineer or Admin.");
        }

        if (snapshot.ExpectedProjectRevision is null ||
            snapshot.ExpectedProjectRevision.Value != snapshot.PersistenceRevision)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_REVISION_REQUIRED",
                "Draft execution requires the current expected project revision.");
        }

        if (!snapshot.CapabilityManifest.IsExplicit)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_CAPABILITY_CONFIRMATION_REQUIRED",
                "Draft execution requires an explicit capability manifest.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ConfirmationId) ||
            string.IsNullOrWhiteSpace(snapshot.AuditId) ||
            string.Equals(snapshot.ConfirmationId, snapshot.AuditId, StringComparison.Ordinal))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_CONFIRMATION_REQUIRED",
                "Draft execution requires distinct confirmation and audit identifiers.");
        }

        if (!snapshot.ResourceBindings.TryGetValue("ProjectRevision", out var boundRevision) ||
            !long.TryParse(boundRevision, out var parsedRevision) ||
            parsedRevision != snapshot.PersistenceRevision ||
            !snapshot.ResourceBindings.TryGetValue("FlowHash", out var boundFlowHash) ||
            !string.Equals(boundFlowHash, snapshot.FlowHash, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_RESOURCE_BINDING_REQUIRED",
                "Draft execution requires server-issued project revision and flow hash bindings.");
        }

        if (snapshot.RunMode is ExecutionRunMode.Preview or ExecutionRunMode.Debug)
        {
            return (requiredCapabilities & ~snapshot.SideEffectPolicy.AllowedCapabilities) == ExecutionSideEffect.None
                ? ExecutionAuthorityDecision.Allow()
                : ExecutionAuthorityDecision.Reject(
                    "ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED",
                    "Preview/debug drafts cannot perform file, network, database, or device side effects.");
        }

        if (snapshot.RunMode != ExecutionRunMode.FormalPrimary)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_SOURCE_MODE_MISMATCH",
                "Draft execution is allowed only as bounded Preview/Debug or confirmed FormalPrimary.");
        }

        if ((flowCapabilities & DraftExternalEffects) != ExecutionSideEffect.None)
        {
            return ExecutionAuthorityDecision.Reject(
                "ADMISSION_DRAFT_EXTERNAL_RESOURCE_FORBIDDEN",
                "Draft flows cannot elevate client parameters into file, network, database, or device authority.");
        }

        return ExecutionAuthorityDecision.Allow();
    }
}
