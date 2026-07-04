using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class InspectionEvidenceManifestService : IInspectionEvidenceManifestService
{
    private const string ManifestAvailable = "available";
    private const string ManifestPartial = "partial";
    private const string ManifestMissing = "missing";
    private const string ManifestExpired = "expired";
    private const string ManifestDisabled = "disabled";
    private const string EvidenceMissingMessage = "证据清单缺失或已清理";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IInspectionResultRepository _repository;
    private readonly ILogger<InspectionEvidenceManifestService> _logger;
    private readonly ProjectSaveCoordinator? _projectSaveCoordinator;
    private readonly StudioEvidenceRetentionOptions _options;
    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;

    public InspectionEvidenceManifestService(
        IInspectionResultRepository repository,
        ILogger<InspectionEvidenceManifestService> logger,
        IConfiguration? configuration = null,
        ProjectSaveCoordinator? projectSaveCoordinator = null)
    {
        _repository = repository;
        _logger = logger;
        _projectSaveCoordinator = projectSaveCoordinator;
        _options = ResolveOptions(configuration);
        _rootPath = ResolveRootPath(_options.RootPath);
        _timeProvider = TimeProvider.System;
    }

    internal InspectionEvidenceManifestService(
        IInspectionResultRepository repository,
        ILogger<InspectionEvidenceManifestService> logger,
        StudioEvidenceRetentionOptions options,
        string rootPath,
        TimeProvider? timeProvider = null,
        ProjectSaveCoordinator? projectSaveCoordinator = null)
    {
        _repository = repository;
        _logger = logger;
        _projectSaveCoordinator = projectSaveCoordinator;
        _options = options;
        _rootPath = ResolveRootPath(rootPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task CaptureAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return;
        }

        var policy = InspectionEvidenceRetentionPolicy.ForStatus(_options, result.Status);
        if (!policy.Enabled)
        {
            return;
        }

        try
        {
            var now = _timeProvider.GetUtcNow();
            var resultRoot = GetResultRoot(result.ProjectId, result.Id);
            var itemsRoot = Path.Combine(resultRoot, "items");
            Directory.CreateDirectory(itemsRoot);

            var items = new List<InspectionEvidenceItemV1>();
            var redaction = CreateRedactionSummary();

            await TryWriteJsonItemAsync(
                items,
                resultRoot,
                "items/summary.json",
                "report-json",
                "summary",
                BuildSummaryPayload(result),
                policy,
                now,
                redacted: false,
                sensitiveFieldsRemoved: [],
                cancellationToken);

            if (policy.CaptureJsonEvidence && !string.IsNullOrWhiteSpace(result.OutputDataJson))
            {
                await TryWriteSanitizedPreviewItemAsync(
                    items,
                    resultRoot,
                    "items/output-data-preview.json",
                    "report-json",
                    "output-data-preview",
                    result.OutputDataJson,
                    policy,
                    now,
                    redaction,
                    cancellationToken);
            }

            if (policy.CaptureJsonEvidence && !string.IsNullOrWhiteSpace(result.AnalysisDataJson))
            {
                await TryWriteSanitizedPreviewItemAsync(
                    items,
                    resultRoot,
                    "items/analysis-data-preview.json",
                    "report-json",
                    "analysis-data-preview",
                    result.AnalysisDataJson,
                    policy,
                    now,
                    redaction,
                    cancellationToken);
            }

            if (policy.CaptureOutputImage && result.OutputImage is { Length: > 0 } outputImage)
            {
                await TryWriteBinaryItemAsync(
                    items,
                    resultRoot,
                    $"items/output-image{InspectionImageFormatDetector.GuessExtension(outputImage)}",
                    "output-image",
                    "output-image",
                    GuessImageContentType(outputImage),
                    outputImage,
                    policy,
                    now,
                    cancellationToken);
            }

            if (items.Count == 0)
            {
                return;
            }

            var manifest = new InspectionEvidenceManifestV1
            {
                SchemaVersion = InspectionEvidenceSchema.ManifestSchemaVersion,
                ManifestId = $"evidence_{result.ProjectId:N}_{result.Id:N}",
                ProjectId = result.ProjectId,
                InspectionResultId = result.Id,
                Status = result.Status.ToString(),
                Outcome = result.Status.ToString(),
                CreatedAtUtc = now,
                FlowVersionHash = result.FlowVersionHash,
                CalibrationBundleId = result.CalibrationBundleId,
                SessionId = result.SessionId,
                RunId = result.SessionId,
                RetentionClass = policy.RetentionClass,
                RetentionExpiresAtUtc = policy.RetentionDays > 0 ? now.AddDays(policy.RetentionDays) : null,
                TotalBytes = items.Where(item => item.Available).Sum(item => item.SizeBytes),
                Items = items.Take(Math.Max(1, policy.MaxItemsPerResult)).ToList(),
                Redaction = redaction
            };
            manifest.Checksum = ComputeManifestChecksum(manifest);

            await WriteJsonAtomicAsync(
                GetManifestPath(result.ProjectId, result.Id),
                manifest,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[InspectionEvidence] Failed to capture evidence manifest. ProjectId={ProjectId}, ResultId={ResultId}",
                result.ProjectId,
                result.Id);
        }
    }

    public async Task<InspectionEvidenceSummary> GetSummaryAsync(
        InspectionHistoryDetail result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return DisabledSummary(result.ProjectId, result.Id);
        }

        var read = await ReadManifestForResultAsync(result, verifyItems: true, cancellationToken);
        return read.Summary;
    }

    public async Task<InspectionEvidenceManifestReadResult> GetManifestAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = ManifestDisabled,
                ErrorCode = "EvidenceDisabled",
                Message = "Evidence manifest capture is disabled.",
                Summary = DisabledSummary(projectId, resultId)
            };
        }

        var access = _projectSaveCoordinator == null
            ? null
            : await _projectSaveCoordinator.AcquireProjectAccessAsync(projectId);
        await using (access)
        {
            var detail = await _repository.GetHistoryDetailAsync(projectId, resultId);
            if (detail == null)
            {
                return new InspectionEvidenceManifestReadResult
                {
                    Found = false,
                    Status = "not-found",
                    ErrorCode = "InspectionResultNotFound",
                    Message = "Inspection history result was not found.",
                    Summary = MissingSummary(projectId, resultId, "Inspection history result was not found.")
                };
            }

            return await ReadManifestForResultAsync(detail, verifyItems: true, cancellationToken);
        }
    }

    public async Task<InspectionEvidenceExportResult> ExportAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        var read = await GetManifestAsync(projectId, resultId, cancellationToken);
        if (!read.Found || read.Manifest == null)
        {
            return new InspectionEvidenceExportResult
            {
                Success = false,
                Status = read.Status,
                ErrorCode = read.ErrorCode ?? "EvidenceManifestUnavailable",
                Message = read.Message ?? EvidenceMissingMessage
            };
        }

        if (read.Status == ManifestExpired)
        {
            return new InspectionEvidenceExportResult
            {
                Success = false,
                Status = ManifestExpired,
                ErrorCode = "EvidenceExpired",
                Message = "Evidence manifest retention has expired."
            };
        }

        var package = new EvidenceExportPackage
        {
            SchemaVersion = 1,
            ExportFormat = "bounded-json-v1",
            ZipImplemented = false,
            ExportedAtUtc = _timeProvider.GetUtcNow(),
            Manifest = read.Manifest,
            Summary = read.Summary,
            Warnings = read.Warnings
        };

        foreach (var item in read.Manifest.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            package.Items.Add(await BuildExportItemAsync(projectId, resultId, item, cancellationToken));
        }

        var withoutChecksum = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (withoutChecksum.LongLength > _options.MaxExportBytes)
        {
            return new InspectionEvidenceExportResult
            {
                Success = false,
                Status = "too-large",
                ErrorCode = "EvidenceExportTooLarge",
                Message = $"Evidence export exceeds maxExportBytes ({_options.MaxExportBytes}).",
                TotalBytes = withoutChecksum.LongLength
            };
        }

        package.ExportSha256 = ComputeSha256Hex(withoutChecksum);
        var content = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (content.LongLength > _options.MaxExportBytes)
        {
            return new InspectionEvidenceExportResult
            {
                Success = false,
                Status = "too-large",
                ErrorCode = "EvidenceExportTooLarge",
                Message = $"Evidence export exceeds maxExportBytes ({_options.MaxExportBytes}).",
                TotalBytes = content.LongLength
            };
        }

        return new InspectionEvidenceExportResult
        {
            Success = true,
            Status = read.Status,
            FileName = $"inspection-evidence-{projectId:N}-{resultId:N}.json",
            ContentType = "application/json",
            Content = content,
            TotalBytes = content.LongLength,
            Sha256 = ComputeSha256Hex(content)
        };
    }

    public async Task<InspectionEvidenceRetentionCleanupResult> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cleanup = new InspectionEvidenceRetentionCleanupResult();
        if (!_options.Enabled || !Directory.Exists(_rootPath))
        {
            return cleanup;
        }

        var candidates = new List<RetentionCandidate>();
        foreach (var manifestPath in Directory.EnumerateFiles(_rootPath, InspectionEvidenceSchema.ManifestFileName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultRoot = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(resultRoot) || !IsSubPath(_rootPath, resultRoot))
            {
                continue;
            }

            var candidate = await BuildRetentionCandidateAsync(manifestPath, resultRoot, cancellationToken);
            candidates.Add(candidate);
        }

        var now = _timeProvider.GetUtcNow();
        var expired = candidates
            .Where(candidate => candidate.ExpiresAtUtc.HasValue && candidate.ExpiresAtUtc <= now)
            .OrderBy(candidate => candidate.RetentionPriority)
            .ThenBy(candidate => candidate.ExpiresAtUtc)
            .ThenBy(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.ManifestId, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in expired)
        {
            DeleteCandidate(candidate, cleanup);
            candidates.Remove(candidate);
        }

        var totalBytes = candidates.Sum(candidate => candidate.TotalBytes);
        if (totalBytes <= _options.MaxTotalBytes)
        {
            return cleanup;
        }

        foreach (var candidate in candidates
            .OrderBy(candidate => candidate.RetentionPriority)
            .ThenBy(candidate => candidate.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.ManifestId, StringComparer.Ordinal))
        {
            if (totalBytes <= _options.MaxTotalBytes)
            {
                break;
            }

            DeleteCandidate(candidate, cleanup);
            totalBytes = Math.Max(0, totalBytes - candidate.TotalBytes);
        }

        return cleanup;
    }

    public static string ComputeManifestChecksum(InspectionEvidenceManifestV1 manifest)
    {
        var originalChecksum = manifest.Checksum;
        manifest.Checksum = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            return ComputeSha256Hex(bytes);
        }
        finally
        {
            manifest.Checksum = originalChecksum;
        }
    }

    private async Task<InspectionEvidenceManifestReadResult> ReadManifestForResultAsync(
        InspectionHistoryDetail result,
        bool verifyItems,
        CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(result.ProjectId, result.Id);
        if (!File.Exists(manifestPath))
        {
            return new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = ManifestMissing,
                ErrorCode = "EvidenceManifestMissing",
                Message = EvidenceMissingMessage,
                Summary = MissingSummary(result.ProjectId, result.Id, EvidenceMissingMessage)
            };
        }

        InspectionEvidenceManifestV1? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<InspectionEvidenceManifestV1>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = ManifestMissing,
                ErrorCode = "EvidenceManifestUnreadable",
                Message = EvidenceMissingMessage,
                Summary = MissingSummary(result.ProjectId, result.Id, EvidenceMissingMessage)
            };
        }

        if (manifest == null ||
            manifest.SchemaVersion != InspectionEvidenceSchema.ManifestSchemaVersion ||
            manifest.ProjectId != result.ProjectId ||
            manifest.InspectionResultId != result.Id)
        {
            return new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = ManifestMissing,
                ErrorCode = "EvidenceManifestInvalid",
                Message = EvidenceMissingMessage,
                Summary = MissingSummary(result.ProjectId, result.Id, EvidenceMissingMessage)
            };
        }

        if (string.IsNullOrWhiteSpace(manifest.Checksum) ||
            !string.Equals(ComputeManifestChecksum(manifest), manifest.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = ManifestMissing,
                ErrorCode = "EvidenceManifestChecksumMismatch",
                Message = EvidenceMissingMessage,
                Summary = MissingSummary(result.ProjectId, result.Id, EvidenceMissingMessage)
            };
        }

        var warnings = new List<string>();
        var itemRoot = GetResultRoot(result.ProjectId, result.Id);
        foreach (var item in manifest.Items)
        {
            if (!ValidateRelativePath(item.RelativePath, allowNull: true))
            {
                item.Available = false;
                item.MissingReason = "invalid-relative-path";
                warnings.Add($"Evidence item {item.Id} has an invalid relative path.");
                continue;
            }

            if (!verifyItems || string.IsNullOrWhiteSpace(item.RelativePath))
            {
                continue;
            }

            var itemPath = Path.GetFullPath(Path.Combine(itemRoot, FromManifestRelativePath(item.RelativePath)));
            if (!IsSubPath(itemRoot, itemPath) || !File.Exists(itemPath))
            {
                item.Available = false;
                item.MissingReason = "file-missing";
                warnings.Add($"Evidence item {item.Id} is missing.");
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(itemPath, cancellationToken);
            var sha256 = ComputeSha256Hex(bytes);
            if (!string.Equals(item.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                item.Available = false;
                item.MissingReason = "sha256-mismatch";
                warnings.Add($"Evidence item {item.Id} checksum mismatch.");
            }
        }

        var status = ResolveManifestStatus(manifest);
        if (status != ManifestExpired)
        {
            var available = manifest.Items.Count(item => item.Available);
            status = available == manifest.Items.Count
                ? ManifestAvailable
                : available > 0
                    ? ManifestPartial
                    : ManifestMissing;
        }

        return new InspectionEvidenceManifestReadResult
        {
            Found = true,
            Status = status,
            Message = status switch
            {
                ManifestAvailable => "Evidence manifest is available.",
                ManifestPartial => "Evidence manifest is partially available.",
                ManifestExpired => "Evidence manifest retention has expired.",
                _ => EvidenceMissingMessage
            },
            Manifest = manifest,
            Summary = ToSummary(result.ProjectId, result.Id, manifest, status),
            Warnings = warnings
        };
    }

    private async Task TryWriteSanitizedPreviewItemAsync(
        List<InspectionEvidenceItemV1> items,
        string resultRoot,
        string relativePath,
        string role,
        string id,
        string json,
        InspectionEvidenceOutcomePolicy policy,
        DateTimeOffset createdAtUtc,
        InspectionEvidenceRedactionSummary redaction,
        CancellationToken cancellationToken)
    {
        var preview = SafeJsonPreviewBuilder.Build(json);
        var removed = preview.WasRedacted
            ? new List<string> { "secret-like-key-or-value", "local-absolute-path", "large-image-scene-artifact-payload" }
            : [];
        if (removed.Count > 0)
        {
            redaction.Applied = true;
            foreach (var item in removed)
            {
                if (!redaction.SensitiveFieldsRemoved.Contains(item, StringComparer.OrdinalIgnoreCase))
                {
                    redaction.SensitiveFieldsRemoved.Add(item);
                }
            }
        }

        await TryWriteJsonItemAsync(
            items,
            resultRoot,
            relativePath,
            role,
            id,
            preview,
            policy,
            createdAtUtc,
            preview.WasRedacted,
            removed,
            cancellationToken);
    }

    private async Task TryWriteJsonItemAsync(
        List<InspectionEvidenceItemV1> items,
        string resultRoot,
        string relativePath,
        string role,
        string id,
        object payload,
        InspectionEvidenceOutcomePolicy policy,
        DateTimeOffset createdAtUtc,
        bool redacted,
        List<string> sensitiveFieldsRemoved,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await TryWriteBinaryItemAsync(
            items,
            resultRoot,
            relativePath,
            role,
            id,
            "application/json",
            bytes,
            policy,
            createdAtUtc,
            cancellationToken,
            redacted,
            sensitiveFieldsRemoved);
    }

    private async Task TryWriteBinaryItemAsync(
        List<InspectionEvidenceItemV1> items,
        string resultRoot,
        string relativePath,
        string role,
        string id,
        string contentType,
        byte[] bytes,
        InspectionEvidenceOutcomePolicy policy,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken,
        bool redacted = false,
        List<string>? sensitiveFieldsRemoved = null)
    {
        if (items.Count >= Math.Max(1, policy.MaxItemsPerResult))
        {
            return;
        }

        if (!ValidateRelativePath(relativePath, allowNull: false))
        {
            throw new InvalidOperationException($"Evidence relative path is invalid: {relativePath}");
        }

        if (bytes.LongLength > policy.MaxItemBytes)
        {
            items.Add(new InspectionEvidenceItemV1
            {
                Id = id,
                Role = role,
                ContentType = contentType,
                RelativePath = null,
                SizeBytes = bytes.LongLength,
                Sha256 = ComputeSha256Hex(bytes),
                CreatedAtUtc = createdAtUtc,
                RetentionClass = policy.RetentionClass,
                Redacted = redacted,
                SensitiveFieldsRemoved = sensitiveFieldsRemoved ?? [],
                Available = false,
                MissingReason = "max-item-bytes-exceeded"
            });
            return;
        }

        var targetPath = Path.GetFullPath(Path.Combine(resultRoot, FromManifestRelativePath(relativePath)));
        if (!IsSubPath(resultRoot, targetPath))
        {
            throw new InvalidOperationException($"Evidence path escaped result root: {relativePath}");
        }

        await WriteBytesAtomicAsync(targetPath, bytes, cancellationToken);
        items.Add(new InspectionEvidenceItemV1
        {
            Id = id,
            Role = role,
            ContentType = contentType,
            RelativePath = ToManifestRelativePath(relativePath),
            SizeBytes = bytes.LongLength,
            Sha256 = ComputeSha256Hex(bytes),
            CreatedAtUtc = createdAtUtc,
            RetentionClass = policy.RetentionClass,
            Redacted = redacted,
            SensitiveFieldsRemoved = sensitiveFieldsRemoved ?? [],
            Available = true
        });
    }

    private async Task<EvidenceExportItem> BuildExportItemAsync(
        Guid projectId,
        Guid resultId,
        InspectionEvidenceItemV1 item,
        CancellationToken cancellationToken)
    {
        var exportItem = new EvidenceExportItem
        {
            Item = item
        };

        if (!item.Available || string.IsNullOrWhiteSpace(item.RelativePath))
        {
            exportItem.OmittedReason = item.MissingReason ?? "item-unavailable";
            return exportItem;
        }

        if (!ValidateRelativePath(item.RelativePath, allowNull: false))
        {
            exportItem.OmittedReason = "invalid-relative-path";
            return exportItem;
        }

        var itemPath = Path.GetFullPath(Path.Combine(GetResultRoot(projectId, resultId), FromManifestRelativePath(item.RelativePath)));
        if (!IsSubPath(GetResultRoot(projectId, resultId), itemPath) || !File.Exists(itemPath))
        {
            exportItem.OmittedReason = "file-missing";
            return exportItem;
        }

        if (!IsTextExportContentType(item.ContentType))
        {
            exportItem.OmittedReason = "binary-item-omitted-from-json-export";
            return exportItem;
        }

        var bytes = await File.ReadAllBytesAsync(itemPath, cancellationToken);
        if (!string.Equals(item.Sha256, ComputeSha256Hex(bytes), StringComparison.OrdinalIgnoreCase))
        {
            exportItem.OmittedReason = "sha256-mismatch";
            return exportItem;
        }

        exportItem.ContentText = Encoding.UTF8.GetString(bytes);
        return exportItem;
    }

    private async Task<RetentionCandidate> BuildRetentionCandidateAsync(
        string manifestPath,
        string resultRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<InspectionEvidenceManifestV1>(stream, JsonOptions, cancellationToken);
            if (manifest != null)
            {
                return new RetentionCandidate(
                    manifestPath,
                    resultRoot,
                    manifest.ManifestId,
                    manifest.RetentionClass,
                    manifest.CreatedAtUtc,
                    manifest.RetentionExpiresAtUtc,
                    EstimateDirectoryBytes(resultRoot),
                    manifest.Items.Count,
                    manifest.InspectionResultId.ToString("N"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "[InspectionEvidence] Failed to read manifest during retention cleanup: {ManifestPath}", manifestPath);
        }

        return new RetentionCandidate(
            manifestPath,
            resultRoot,
            Path.GetFileName(resultRoot),
            "corrupt",
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            EstimateDirectoryBytes(resultRoot),
            0,
            Path.GetFileName(resultRoot));
    }

    private void DeleteCandidate(RetentionCandidate candidate, InspectionEvidenceRetentionCleanupResult cleanup)
    {
        if (!IsSubPath(_rootPath, candidate.ResultRoot))
        {
            return;
        }

        var bytes = EstimateDirectoryBytes(candidate.ResultRoot);
        var itemCount = EstimateFileCount(candidate.ResultRoot);
        try
        {
            Directory.Delete(candidate.ResultRoot, recursive: true);
            cleanup.DeletedManifestCount++;
            cleanup.DeletedItemCount += Math.Max(0, itemCount - 1);
            cleanup.FreedBytes += bytes;
            cleanup.DeletedResultIds.Add(candidate.ResultId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[InspectionEvidence] Failed to delete expired evidence root: {ResultRoot}", candidate.ResultRoot);
        }
    }

    private static InspectionEvidenceRedactionSummary CreateRedactionSummary()
    {
        return new InspectionEvidenceRedactionSummary
        {
            Applied = true,
            Rules =
            [
                "secret-like-key",
                "secret-like-value",
                "local-absolute-path",
                "large-image-scene-artifact-payload",
                "json-bounds"
            ]
        };
    }

    private static object BuildSummaryPayload(InspectionResult result)
    {
        return new
        {
            schemaVersion = 1,
            resultId = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            inspectionTimeUtc = result.InspectionTime,
            processingTimeMs = result.ProcessingTimeMs,
            confidenceScore = result.ConfidenceScore,
            errorMessage = result.ErrorMessage,
            flowVersionHash = result.FlowVersionHash,
            calibrationBundleId = result.CalibrationBundleId,
            sessionId = result.SessionId,
            runId = result.SessionId,
            defectCount = result.Defects.Count,
            defects = result.Defects.Take(64).Select(defect => new
            {
                defect.Id,
                type = defect.Type.ToString(),
                defect.X,
                defect.Y,
                defect.Width,
                defect.Height,
                defect.ConfidenceScore,
                defect.Description
            }).ToList()
        };
    }

    private InspectionEvidenceSummary ToSummary(
        Guid projectId,
        Guid resultId,
        InspectionEvidenceManifestV1 manifest,
        string status)
    {
        return new InspectionEvidenceSummary
        {
            HasEvidenceManifest = status is ManifestAvailable or ManifestPartial or ManifestExpired,
            EvidenceStatus = status,
            EvidenceManifestReference = BuildManifestReference(projectId, resultId),
            EvidenceTotalBytes = manifest.TotalBytes,
            RetentionExpiresAtUtc = manifest.RetentionExpiresAtUtc,
            RetentionClass = manifest.RetentionClass,
            Message = status switch
            {
                ManifestAvailable => "Evidence manifest is available.",
                ManifestPartial => "Evidence manifest is partially available.",
                ManifestExpired => "Evidence manifest retention has expired.",
                _ => EvidenceMissingMessage
            },
            Checksum = manifest.Checksum
        };
    }

    private static InspectionEvidenceSummary MissingSummary(Guid projectId, Guid resultId, string message)
    {
        return new InspectionEvidenceSummary
        {
            HasEvidenceManifest = false,
            EvidenceStatus = ManifestMissing,
            EvidenceManifestReference = BuildManifestReference(projectId, resultId),
            Message = message
        };
    }

    private static InspectionEvidenceSummary DisabledSummary(Guid projectId, Guid resultId)
    {
        return new InspectionEvidenceSummary
        {
            HasEvidenceManifest = false,
            EvidenceStatus = ManifestDisabled,
            EvidenceManifestReference = BuildManifestReference(projectId, resultId),
            Message = "Evidence manifest capture is disabled."
        };
    }

    private string GetManifestPath(Guid projectId, Guid resultId)
    {
        return Path.Combine(GetResultRoot(projectId, resultId), InspectionEvidenceSchema.ManifestFileName);
    }

    private string GetResultRoot(Guid projectId, Guid resultId)
    {
        return Path.GetFullPath(Path.Combine(_rootPath, projectId.ToString("N"), resultId.ToString("N")));
    }

    private static string BuildManifestReference(Guid projectId, Guid resultId)
    {
        return $"/api/inspection/history/{projectId:D}/{resultId:D}/evidence/manifest";
    }

    private string ResolveManifestStatus(InspectionEvidenceManifestV1 manifest)
    {
        return manifest.RetentionExpiresAtUtc.HasValue && manifest.RetentionExpiresAtUtc <= _timeProvider.GetUtcNow()
            ? ManifestExpired
            : ManifestAvailable;
    }

    private static bool ValidateRelativePath(string? relativePath, bool allowNull)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return allowNull;
        }

        var normalized = ToManifestRelativePath(relativePath);
        if (Path.IsPathRooted(relativePath) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Equals("..", StringComparison.Ordinal) ||
            normalized.Contains(":/", StringComparison.Ordinal) ||
            normalized.Contains('\\'))
        {
            return false;
        }

        return true;
    }

    private static string ToManifestRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string FromManifestRelativePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsSubPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteJsonAtomicAsync<T>(string targetPath, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await WriteBytesAtomicAsync(targetPath, bytes, cancellationToken);
    }

    private static async Task WriteBytesAtomicAsync(string targetPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static string GuessImageContentType(byte[] imageBytes)
    {
        var extension = InspectionImageFormatDetector.GuessExtension(imageBytes);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "application/octet-stream";
    }

    private static bool IsTextExportContentType(string contentType)
    {
        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
               contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static long EstimateDirectoryBytes(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static int EstimateFileCount(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count()
            : 0;
    }

    private static string ResolveRootPath(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.GetFullPath(Path.Combine(localAppData, "ClearVision", "evidence"));
    }

    private static StudioEvidenceRetentionOptions ResolveOptions(IConfiguration? configuration)
    {
        var options = new StudioEvidenceRetentionOptions
        {
            Enabled = ReadBool(configuration, "Evidence:Studio:Enabled", true),
            RootPath = ReadString(configuration, "Evidence:Studio:RootPath", null),
            MaxTotalBytes = ReadLong(configuration, "Evidence:Studio:MaxTotalBytes", 1024L * 1024L * 1024L, 1, long.MaxValue),
            MaxExportBytes = ReadLong(configuration, "Evidence:Studio:MaxExportBytes", 64L * 1024L * 1024L, 1, long.MaxValue)
        };

        ApplyOutcomePolicy(configuration, "Evidence:Studio:Outcomes:OK", options.OK);
        ApplyOutcomePolicy(configuration, "Evidence:Studio:Outcomes:NG", options.NG);
        ApplyOutcomePolicy(configuration, "Evidence:Studio:Outcomes:Error", options.Error);
        return options;
    }

    private static void ApplyOutcomePolicy(
        IConfiguration? configuration,
        string prefix,
        InspectionEvidenceOutcomePolicy policy)
    {
        policy.Enabled = ReadBool(configuration, $"{prefix}:Enabled", policy.Enabled);
        policy.RetentionClass = ReadString(configuration, $"{prefix}:RetentionClass", policy.RetentionClass) ?? policy.RetentionClass;
        policy.RetentionDays = ReadInt(configuration, $"{prefix}:RetentionDays", policy.RetentionDays, 0, 3650);
        policy.MaxItemBytes = ReadLong(configuration, $"{prefix}:MaxItemBytes", policy.MaxItemBytes, 1, long.MaxValue);
        policy.MaxItemsPerResult = ReadInt(configuration, $"{prefix}:MaxItemsPerResult", policy.MaxItemsPerResult, 1, 256);
        policy.CaptureOutputImage = ReadBool(configuration, $"{prefix}:CaptureOutputImage", policy.CaptureOutputImage);
        policy.CaptureJsonEvidence = ReadBool(configuration, $"{prefix}:CaptureJsonEvidence", policy.CaptureJsonEvidence);
    }

    private static string? ReadString(IConfiguration? configuration, string key, string? fallback)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool ReadBool(IConfiguration? configuration, string key, bool fallback)
    {
        var value = configuration?[key];
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(IConfiguration? configuration, string key, int fallback, int min, int max)
    {
        var value = configuration?[key];
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static long ReadLong(IConfiguration? configuration, string key, long fallback, long min, long max)
    {
        var value = configuration?[key];
        return long.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private sealed class EvidenceExportPackage
    {
        public int SchemaVersion { get; set; }

        public string ExportFormat { get; set; } = "bounded-json-v1";

        public bool ZipImplemented { get; set; }

        public DateTimeOffset ExportedAtUtc { get; set; }

        public InspectionEvidenceManifestV1 Manifest { get; set; } = new();

        public InspectionEvidenceSummary Summary { get; set; } = new();

        public List<EvidenceExportItem> Items { get; set; } = [];

        public List<string> Warnings { get; set; } = [];

        public string? ExportSha256 { get; set; }
    }

    private sealed class EvidenceExportItem
    {
        public InspectionEvidenceItemV1 Item { get; set; } = new();

        public string? ContentText { get; set; }

        public string? OmittedReason { get; set; }
    }

    private sealed record RetentionCandidate(
        string ManifestPath,
        string ResultRoot,
        string ManifestId,
        string RetentionClass,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        long TotalBytes,
        int ItemCount,
        string ResultId)
    {
        public int RetentionPriority => RetentionClass.ToLowerInvariant() switch
        {
            "short" or "station-short" or "corrupt" => 0,
            "standard" => 1,
            "long" or "station-long" => 2,
            _ => 3
        };
    }
}
