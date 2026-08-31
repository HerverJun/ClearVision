namespace ClearVision.Product.Core.Services;

/// <summary>
/// Resolves opaque execution profile ids into server-owned resource targets.
/// Operator parameters may select a profile (and an allow-listed sub-resource),
/// but never supply the target credentials or device endpoint used for dispatch.
/// </summary>
public interface IExecutionResourceProfileResolver
{
    ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource> ResolveDatabase(
        string profileId,
        string requestedTableName);

    ExecutionResourceProfileResolution<ResolvedSerialExecutionResource> ResolveSerial(string profileId);

    ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolvePlc(
        string profileId,
        PlcExecutionResourceRequest request);
}

public static class ExecutionPlcProtocols
{
    public const string SiemensS7 = "S7";
    public const string MitsubishiMc = "MC";
    public const string OmronFins = "FINS";
    public const string ModbusTcp = "MODBUS_TCP";
}

public static class ExecutionResourceCompatibilityProfileIds
{
    public const string CommunicationS7 = "communication:s7";
    public const string CommunicationMc = "communication:mc";
    public const string CommunicationFins = "communication:fins";
}

public sealed record ExecutionResourceProfileResolution<TResource>(
    bool Resolved,
    TResource? Resource,
    string Code,
    string Message)
    where TResource : class
{
    public static ExecutionResourceProfileResolution<TResource> Allow(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new(true, resource, "RESOURCE_PROFILE_RESOLVED", string.Empty);
    }

    public static ExecutionResourceProfileResolution<TResource> Reject(string code, string message) =>
        new(false, null, code, message);
}

public sealed record ResolvedDatabaseExecutionResource(
    string ProfileId,
    string DbType,
    string ConnectionString,
    string TableName);

public sealed record ResolvedSerialExecutionResource(
    string ProfileId,
    string PortName,
    int BaudRate,
    int DataBits,
    string StopBits,
    string Parity);

public sealed record PlcExecutionResourceRequest(
    string Protocol,
    string Address,
    string Operation,
    int ElementCount = 1);

public sealed record ResolvedPlcExecutionResource(
    string ProfileId,
    string Protocol,
    string Host,
    int Port,
    string CpuType,
    int Rack,
    int Slot,
    int UnitId,
    string Address,
    string Operation,
    string DataType,
    int MaxElementCount);

/// <summary>
/// Safe compatibility fallback for manually constructed operators. Production
/// dependency injection replaces it with the server-backed resolver.
/// </summary>
public sealed class DenyAllExecutionResourceProfileResolver : IExecutionResourceProfileResolver
{
    public static DenyAllExecutionResourceProfileResolver Instance { get; } = new();

    private DenyAllExecutionResourceProfileResolver()
    {
    }

    public ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource> ResolveDatabase(
        string profileId,
        string requestedTableName) =>
        ExecutionResourceProfileResolution<ResolvedDatabaseExecutionResource>.Reject(
            "RESOURCE_CONFIGURATION_UNAVAILABLE",
            "Authoritative database resource configuration is unavailable.");

    public ExecutionResourceProfileResolution<ResolvedSerialExecutionResource> ResolveSerial(string profileId) =>
        ExecutionResourceProfileResolution<ResolvedSerialExecutionResource>.Reject(
            "RESOURCE_CONFIGURATION_UNAVAILABLE",
            "Authoritative serial resource configuration is unavailable.");

    public ExecutionResourceProfileResolution<ResolvedPlcExecutionResource> ResolvePlc(
        string profileId,
        PlcExecutionResourceRequest request) =>
        ExecutionResourceProfileResolution<ResolvedPlcExecutionResource>.Reject(
            "RESOURCE_CONFIGURATION_UNAVAILABLE",
            "Authoritative PLC resource configuration is unavailable.");
}
