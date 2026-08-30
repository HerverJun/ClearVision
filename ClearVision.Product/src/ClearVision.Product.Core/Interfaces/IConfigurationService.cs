// IConfigurationService.cs
// 获取当前内存中的配置（同步方法，用于频繁访问）
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// 应用配置服务接口
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Reloads the authoritative file and reports whether the returned value is healthy,
    /// newly initialized, or an explicitly degraded last-good snapshot.
    /// </summary>
    Task<AppConfigReadResult> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibility read for non-HTTP consumers. Degraded and unavailable reads throw
    /// <see cref="AppConfigUnavailableException"/> instead of silently returning defaults.
    /// </summary>
    Task<AppConfig> LoadAsync();

    /// <summary>
    /// Applies an absent-preserving server-side patch under the authoritative mutation gate.
    /// </summary>
    Task<AppConfigMutationResult> MutateAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a patch, durably persists it, then applies dependent runtime state while the
    /// same mutation gate is held. A failed runtime apply restores the previous durable
    /// snapshot; a failed restore fences subsequent mutations until a healthy reload.
    /// </summary>
    Task<AppConfigMutationResult> MutateAndApplyAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate,
        Func<AppConfig, CancellationToken, Task> apply,
        Func<AppConfig, CancellationToken, Task>? rollbackApply = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy test/bootstrap compatibility only. Production mutation paths must use
    /// <see cref="MutateAsync"/> or <see cref="MutateAndApplyAsync"/> with revision CAS.
    /// </summary>
    [Obsolete("Production writers must use revisioned MutateAsync.")]
    Task SaveAsync(AppConfig config);

    /// <summary>
    /// 获取当前内存中的配置（同步方法，用于频繁访问）
    /// </summary>
    AppConfig GetCurrent();
}

public enum AppConfigReadStatus
{
    Healthy,
    Initialized,
    DegradedLastGood,
    Unavailable
}

public sealed record AppConfigReadResult(
    AppConfigReadStatus Status,
    AppConfig? Config,
    string? ErrorCode = null,
    string? Message = null)
{
    public bool IsHealthy => Status is AppConfigReadStatus.Healthy or AppConfigReadStatus.Initialized;
    public bool IsDegraded => Status is AppConfigReadStatus.DegradedLastGood or AppConfigReadStatus.Unavailable;
    public bool HasLastGood => Status == AppConfigReadStatus.DegradedLastGood && Config != null;
}

public enum AppConfigMutationStatus
{
    Applied,
    NoChange,
    RevisionConflict,
    ValidationFailed,
    StorageFailure,
    ApplyFailed,
    Fenced
}

public sealed record AppConfigValidationError(string Field, string Message, string Code = "APP_CONFIG_VALIDATION_ERROR");

public sealed record AppConfigMutationResult(
    AppConfigMutationStatus Status,
    AppConfig? Config,
    long ExpectedRevision,
    long? ActualRevision,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyList<AppConfigValidationError>? ValidationErrors = null)
{
    public bool IsSuccess => Status is AppConfigMutationStatus.Applied or AppConfigMutationStatus.NoChange;
    public bool IsNoOp => Status == AppConfigMutationStatus.NoChange;
}

public sealed class AppConfigUnavailableException : InvalidOperationException
{
    public AppConfigUnavailableException(AppConfigReadResult result)
        : base(result.Message ?? "The application configuration is unavailable.")
    {
        Result = result;
    }

    public AppConfigReadResult Result { get; }
}
