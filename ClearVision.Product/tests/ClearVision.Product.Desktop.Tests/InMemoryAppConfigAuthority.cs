using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Desktop.Tests;

internal sealed class InMemoryAppConfigAuthority : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppConfig _current;
    private bool _fenced;

    public InMemoryAppConfigAuthority(AppConfig? initial = null)
    {
        _current = Clone(initial ?? new AppConfig());
    }

    public bool FailPersist { get; set; }
    public bool IsFenced => _fenced;
    public AppConfigReadResult? ForcedReadResult { get; set; }
    public int MutationCount { get; private set; }

    public Task<AppConfigReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ForcedReadResult ?? new AppConfigReadResult(
            AppConfigReadStatus.Healthy,
            Clone(_current)));
    }

    public Task<AppConfig> LoadAsync() => Task.FromResult(Clone(_current));

    public Task<AppConfigMutationResult> MutateAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate = null,
        CancellationToken cancellationToken = default) =>
        MutateAndApplyAsync(
            expectedRevision,
            patch,
            validate,
            (_, _) => Task.CompletedTask,
            rollbackApply: null,
            cancellationToken);

    public async Task<AppConfigMutationResult> MutateAndApplyAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate,
        Func<AppConfig, CancellationToken, Task> apply,
        Func<AppConfig, CancellationToken, Task>? rollbackApply = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = Clone(_current);
            if (_fenced)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.Fenced,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    "APP_CONFIG_FENCED",
                    "AppConfig mutations are fenced.");
            }

            if (ForcedReadResult is { IsHealthy: false } forced)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.StorageFailure,
                    forced.Config,
                    expectedRevision,
                    forced.Config?.Revision,
                    forced.ErrorCode,
                    forced.Message);
            }

            if (expectedRevision != previous.Revision)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.RevisionConflict,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    "APP_CONFIG_REVISION_CONFLICT",
                    "Revision conflict.");
            }

            var candidate = Clone(previous);
            patch(candidate);
            candidate.Normalize();
            candidate.Revision = previous.Revision;
            var errors = validate?.Invoke(Clone(candidate)) ?? Array.Empty<AppConfigValidationError>();
            if (errors.Count > 0)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.ValidationFailed,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    "APP_CONFIG_VALIDATION_FAILED",
                    "Validation failed.",
                    errors);
            }

            if (JsonSerializer.Serialize(previous, JsonOptions) == JsonSerializer.Serialize(candidate, JsonOptions))
            {
                try
                {
                    await apply(Clone(previous), cancellationToken);
                }
                catch
                {
                    if (rollbackApply != null)
                    {
                        try
                        {
                            await rollbackApply(Clone(previous), CancellationToken.None);
                        }
                        catch
                        {
                            _fenced = true;
                            return new AppConfigMutationResult(
                                AppConfigMutationStatus.Fenced,
                                previous,
                                expectedRevision,
                                previous.Revision,
                                "APP_CONFIG_FENCED",
                                "Runtime rollback failed.");
                        }
                    }

                    return new AppConfigMutationResult(
                        AppConfigMutationStatus.ApplyFailed,
                        previous,
                        expectedRevision,
                        previous.Revision,
                        "APP_CONFIG_RUNTIME_APPLY_FAILED",
                        "Runtime apply failed.");
                }

                return new AppConfigMutationResult(
                    AppConfigMutationStatus.NoChange,
                    previous,
                    expectedRevision,
                    previous.Revision);
            }

            if (FailPersist)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.StorageFailure,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    "APP_CONFIG_PERSIST_FAILED",
                    "Injected persist failure.");
            }

            candidate.Revision = previous.Revision + 1;
            _current = Clone(candidate);
            MutationCount++;
            try
            {
                await apply(Clone(candidate), cancellationToken);
            }
            catch
            {
                _current = Clone(previous);
                if (rollbackApply != null)
                {
                    try
                    {
                        await rollbackApply(Clone(previous), CancellationToken.None);
                    }
                    catch
                    {
                        _fenced = true;
                        return new AppConfigMutationResult(
                            AppConfigMutationStatus.Fenced,
                            previous,
                            expectedRevision,
                            previous.Revision,
                            "APP_CONFIG_FENCED",
                            "Runtime rollback failed.");
                    }
                }

                return new AppConfigMutationResult(
                    AppConfigMutationStatus.ApplyFailed,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    "APP_CONFIG_RUNTIME_APPLY_FAILED",
                    "Runtime apply failed.");
            }

            return new AppConfigMutationResult(
                AppConfigMutationStatus.Applied,
                Clone(candidate),
                expectedRevision,
                candidate.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    [Obsolete("Production writers must use revisioned MutateAsync.")]
    public Task SaveAsync(AppConfig config)
    {
        _current = Clone(config);
        return Task.CompletedTask;
    }

    public AppConfig GetCurrent() => Clone(_current);

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var clone = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        clone.Normalize();
        return clone;
    }
}
