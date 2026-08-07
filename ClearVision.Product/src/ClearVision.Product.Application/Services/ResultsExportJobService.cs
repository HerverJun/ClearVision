using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Services;

public enum ResultsExportFormat
{
    Csv,
    Json
}

public enum ResultsExportJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record ResultsExportRequest(
    Guid ProjectId,
    string Source,
    ResultsExportFormat Format,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Status,
    string? DefectType,
    string? DiagnosticCode,
    Guid ClientOperationId);

public sealed record ResultsExportJobSnapshot(
    Guid ExportId,
    Guid ProjectId,
    string Source,
    ResultsExportFormat Format,
    Guid ClientOperationId,
    ResultsExportJobState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTime? SnapshotUpperBoundUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ArtifactExpiresAtUtc,
    string FileName,
    string? ErrorCode,
    string? ErrorMessage,
    bool DownloadAvailable);

public sealed record ResultsExportJobStartResult(
    ResultsExportJobSnapshot Job,
    bool OperationReplayed);

public sealed record ResultsExportArtifact(
    byte[] Bytes,
    string ContentType,
    string FileName,
    string Sha256,
    DateTimeOffset ExpiresAtUtc);

public interface IResultsExportJobService
{
    Task<ResultsExportJobStartResult> CreateAsync(
        ResultsExportRequest request,
        CancellationToken cancellationToken = default);

    ResultsExportJobSnapshot? Get(Guid exportId);

    ResultsExportJobSnapshot? FindByClientOperationId(Guid clientOperationId);

    ResultsExportJobSnapshot? Cancel(Guid exportId);

    bool TryReadArtifact(Guid exportId, out ResultsExportArtifact? artifact);
}

public sealed class ResultsExportValidationException : Exception
{
    public ResultsExportValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ResultsExportIdentityConflictException : Exception
{
    public ResultsExportIdentityConflictException(string message)
        : base(message)
    {
    }
}

public sealed class ResultsExportProjectNotFoundException : Exception
{
    public ResultsExportProjectNotFoundException(Guid projectId)
        : base($"Project {projectId:D} was not found.")
    {
        ProjectId = projectId;
    }

    public Guid ProjectId { get; }
}

/// <summary>
/// Coordinates the narrow server-side Results export lifecycle. Job metadata and
/// artifacts are process-local because the Desktop host is the local authority;
/// artifacts are bounded and expire without touching the source tree.
/// </summary>
public sealed class ResultsExportJobService : IResultsExportJobService, IDisposable
{
    private static readonly TimeSpan ArtifactTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan JobRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResultsExportJobService> _logger;
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly Dictionary<Guid, Guid> _operationIndex = [];
    private readonly SemaphoreSlim _createGate = new(1, 1);
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public ResultsExportJobService(
        IServiceScopeFactory scopeFactory,
        ILogger<ResultsExportJobService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cleanupTimer = new Timer(
            static state => ((ResultsExportJobService)state!).CleanupExpired(),
            this,
            CleanupInterval,
            CleanupInterval);
    }

    public async Task<ResultsExportJobStartResult> CreateAsync(
        ResultsExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalized = Normalize(request);
        var fingerprint = Fingerprint(normalized);

        await _createGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
                if (_operationIndex.TryGetValue(normalized.ClientOperationId, out var existingId) &&
                    _jobs.TryGetValue(existingId, out var existing))
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        throw new ResultsExportIdentityConflictException(
                            "clientOperationId 已用于不同的结果导出请求，请生成新的操作标识。 ");
                    }

                    return new ResultsExportJobStartResult(ToSnapshotUnderLock(existing), true);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (var scope = _scopeFactory.CreateScope())
            {
                var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
                if (await projectRepository.GetByIdFreshAsync(normalized.ProjectId) == null)
                {
                    throw new ResultsExportProjectNotFoundException(normalized.ProjectId);
                }
            }

            var job = new Job(
                Guid.NewGuid(),
                normalized,
                fingerprint,
                DateTimeOffset.UtcNow,
                DateTime.UtcNow);
            lock (_gate)
            {
                CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
                if (_operationIndex.TryGetValue(normalized.ClientOperationId, out var existingId) &&
                    _jobs.TryGetValue(existingId, out var existing))
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        throw new ResultsExportIdentityConflictException(
                            "clientOperationId 已用于不同的结果导出请求，请生成新的操作标识。 ");
                    }

                    return new ResultsExportJobStartResult(ToSnapshotUnderLock(existing), true);
                }

                _jobs[job.ExportId] = job;
                _operationIndex[normalized.ClientOperationId] = job.ExportId;
            }

            _ = Task.Run(() => ExecuteAsync(job), CancellationToken.None);
            return new ResultsExportJobStartResult(ToSnapshot(job), false);
        }
        finally
        {
            _createGate.Release();
        }
    }

    public ResultsExportJobSnapshot? Get(Guid exportId)
    {
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            return _jobs.TryGetValue(exportId, out var job) ? ToSnapshotUnderLock(job) : null;
        }
    }

    public ResultsExportJobSnapshot? FindByClientOperationId(Guid clientOperationId)
    {
        if (clientOperationId == Guid.Empty)
        {
            return null;
        }

        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            return _operationIndex.TryGetValue(clientOperationId, out var exportId) &&
                   _jobs.TryGetValue(exportId, out var job)
                ? ToSnapshotUnderLock(job)
                : null;
        }
    }

    public ResultsExportJobSnapshot? Cancel(Guid exportId)
    {
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            if (!_jobs.TryGetValue(exportId, out var job))
            {
                return null;
            }

            if (job.State is ResultsExportJobState.Queued or ResultsExportJobState.Running)
            {
                job.State = ResultsExportJobState.Cancelled;
                job.ErrorCode = "RESULTS_EXPORT_CANCELLED";
                job.ErrorMessage = "结果导出已取消，未生成可下载文件。";
                job.UpdatedAtUtc = DateTimeOffset.UtcNow;
                job.Cancellation.Cancel();
            }

            return ToSnapshotUnderLock(job);
        }
    }

    public bool TryReadArtifact(Guid exportId, out ResultsExportArtifact? artifact)
    {
        artifact = null;
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            if (!_jobs.TryGetValue(exportId, out var job) ||
                job.State != ResultsExportJobState.Completed ||
                job.ArtifactBytes == null ||
                job.ArtifactExpiresAtUtc is not { } expiresAt ||
                expiresAt <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            artifact = new ResultsExportArtifact(
                job.ArtifactBytes.ToArray(),
                job.ContentType,
                job.FileName,
                job.Sha256,
                expiresAt);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var job in _jobs.Values)
            {
                job.Cancellation.Cancel();
                job.Cancellation.Dispose();
            }

            _jobs.Clear();
            _operationIndex.Clear();
        }

        _cleanupTimer.Dispose();
        _createGate.Dispose();
    }

    private async Task ExecuteAsync(Job job)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || job.State == ResultsExportJobState.Cancelled)
                {
                    return;
                }

                job.State = ResultsExportJobState.Running;
                job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            using var scope = _scopeFactory.CreateScope();
            var analysisService = scope.ServiceProvider.GetRequiredService<IResultAnalysisService>();
            var cancellationToken = job.Cancellation.Token;
            var endTime = ResolveSnapshotEnd(job.Request.EndTime, job.SnapshotUpperBoundUtc);
            var content = job.Request.Format == ResultsExportFormat.Csv
                ? await analysisService.ExportToCsvAsync(
                    job.Request.ProjectId,
                    job.Request.StartTime,
                    endTime,
                    job.Request.Status,
                    job.Request.DefectType,
                    cancellationToken,
                    job.Request.DiagnosticCode)
                : await analysisService.ExportToJsonAsync(
                    job.Request.ProjectId,
                    job.Request.StartTime,
                    endTime,
                    job.Request.Status,
                    job.Request.DefectType,
                    cancellationToken,
                    job.Request.DiagnosticCode);
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content);
            var now = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                if (_disposed || job.State == ResultsExportJobState.Cancelled)
                {
                    return;
                }

                job.ArtifactBytes = bytes;
                job.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                job.ArtifactExpiresAtUtc = now.Add(ArtifactTtl);
                job.CompletedAtUtc = now;
                job.UpdatedAtUtc = now;
                job.State = ResultsExportJobState.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (!_disposed && job.State != ResultsExportJobState.Cancelled)
                {
                    job.State = ResultsExportJobState.Cancelled;
                    job.ErrorCode = "RESULTS_EXPORT_CANCELLED";
                    job.ErrorMessage = "结果导出已取消，未生成可下载文件。";
                    job.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Results export job {ExportId} failed.", job.ExportId);
            lock (_gate)
            {
                if (!_disposed && job.State != ResultsExportJobState.Cancelled)
                {
                    job.State = ResultsExportJobState.Failed;
                    job.ErrorCode = "RESULTS_EXPORT_FAILED";
                    job.ErrorMessage = "服务端无法完成当前结果导出，请检查结果存储状态后重试。";
                    job.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    private void CleanupExpired()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            }
        }
    }

    private void CleanupExpiredUnderLock(DateTimeOffset now)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.ArtifactExpiresAtUtc is { } artifactExpiry && artifactExpiry <= now)
            {
                job.ArtifactBytes = null;
                job.ArtifactExpiresAtUtc = artifactExpiry;
            }
        }

        foreach (var job in _jobs.Values
                     .Where(job => job.State is not (ResultsExportJobState.Queued or ResultsExportJobState.Running) &&
                                   job.UpdatedAtUtc.Add(JobRetention) <= now)
                     .ToList())
        {
            _jobs.Remove(job.ExportId);
            _operationIndex.Remove(job.Request.ClientOperationId);
            job.Cancellation.Dispose();
        }
    }

    private ResultsExportJobSnapshot ToSnapshot(Job job)
    {
        lock (_gate)
        {
            return ToSnapshotUnderLock(job);
        }
    }

    private static ResultsExportJobSnapshot ToSnapshotUnderLock(Job job)
    {
        var available = job.State == ResultsExportJobState.Completed &&
                        job.ArtifactBytes != null &&
                        job.ArtifactExpiresAtUtc is { } expiresAt &&
                        expiresAt > DateTimeOffset.UtcNow;
        return new ResultsExportJobSnapshot(
            job.ExportId,
            job.Request.ProjectId,
            job.Request.Source,
            job.Request.Format,
            job.Request.ClientOperationId,
            job.State,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.SnapshotUpperBoundUtc,
            job.CompletedAtUtc,
            job.ArtifactExpiresAtUtc,
            job.FileName,
            job.ErrorCode,
            job.ErrorMessage,
            available);
    }

    private static ResultsExportRequest Normalize(ResultsExportRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            throw new ResultsExportValidationException("RESULTS_EXPORT_PROJECT_REQUIRED", "结果导出必须指定工程。");
        }

        if (request.ClientOperationId == Guid.Empty)
        {
            throw new ResultsExportValidationException("RESULTS_EXPORT_OPERATION_ID_REQUIRED", "结果导出必须提供 clientOperationId。");
        }

        var source = request.Source?.Trim().ToLowerInvariant();
        if (source != "local")
        {
            throw new ResultsExportValidationException(
                "RESULTS_EXPORT_SOURCE_UNSUPPORTED",
                "当前仅支持本机结果导出；工作站上报结果没有同等导出合同。");
        }

        var startTime = NormalizeUtc(request.StartTime);
        var endTime = NormalizeUtc(request.EndTime);
        if (startTime.HasValue && endTime.HasValue && startTime > endTime)
        {
            throw new ResultsExportValidationException("RESULTS_EXPORT_DATE_RANGE_INVALID", "开始时间不能晚于结束时间。");
        }

        return request with
        {
            Source = source,
            Format = request.Format,
            StartTime = startTime,
            EndTime = endTime,
            Status = NormalizeOptional(request.Status),
            DefectType = NormalizeOptional(request.DefectType),
            DiagnosticCode = NormalizeOptional(request.DiagnosticCode)
        };
    }

    private static string Fingerprint(ResultsExportRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.ProjectId,
            request.Source,
            request.Format,
            request.StartTime,
            request.EndTime,
            request.Status,
            request.DefectType,
            request.DiagnosticCode
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static DateTime ResolveSnapshotEnd(DateTime? requestedEnd, DateTime snapshotUpperBoundUtc)
    {
        var normalizedEnd = requestedEnd ?? snapshotUpperBoundUtc;
        return normalizedEnd <= snapshotUpperBoundUtc ? normalizedEnd : snapshotUpperBoundUtc;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed class Job
    {
        public Job(
            Guid exportId,
            ResultsExportRequest request,
            string fingerprint,
            DateTimeOffset createdAtUtc,
            DateTime snapshotUpperBoundUtc)
        {
            ExportId = exportId;
            Request = request;
            Fingerprint = fingerprint;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = createdAtUtc;
            SnapshotUpperBoundUtc = snapshotUpperBoundUtc;
            FileName = $"clearvision-results-{request.ProjectId:N}-{exportId:N}.{request.Format.ToString().ToLowerInvariant()}";
            ContentType = request.Format == ResultsExportFormat.Csv
                ? "text/csv"
                : "application/json";
        }

        public Guid ExportId { get; }
        public ResultsExportRequest Request { get; }
        public string Fingerprint { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public DateTime SnapshotUpperBoundUtc { get; }
        public ResultsExportJobState State { get; set; } = ResultsExportJobState.Queued;
        public string FileName { get; }
        public string ContentType { get; }
        public byte[]? ArtifactBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public DateTimeOffset? ArtifactExpiresAtUtc { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
    }
}
