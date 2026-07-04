using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Interfaces;

public static class InspectionEvidenceSchema
{
    public const int ManifestSchemaVersion = 1;
    public const string ManifestFileName = "manifest.json";
}

public sealed class InspectionEvidenceManifestV1
{
    public int SchemaVersion { get; set; } = InspectionEvidenceSchema.ManifestSchemaVersion;

    public string ManifestId { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }

    public Guid InspectionResultId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? FlowVersionHash { get; set; }

    public string? CalibrationBundleId { get; set; }

    public Guid? SessionId { get; set; }

    public Guid? RunId { get; set; }

    public string RetentionClass { get; set; } = "standard";

    public DateTimeOffset? RetentionExpiresAtUtc { get; set; }

    public long TotalBytes { get; set; }

    public List<InspectionEvidenceItemV1> Items { get; set; } = [];

    public string? Checksum { get; set; }

    public InspectionEvidenceRedactionSummary Redaction { get; set; } = new();
}

public sealed class InspectionEvidenceItemV1
{
    public string Id { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public string? RelativePath { get; set; }

    public long SizeBytes { get; set; }

    public string? Sha256 { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string RetentionClass { get; set; } = "standard";

    public bool Redacted { get; set; }

    public List<string> SensitiveFieldsRemoved { get; set; } = [];

    public bool Available { get; set; } = true;

    public string? MissingReason { get; set; }
}

public sealed class InspectionEvidenceRedactionSummary
{
    public bool Applied { get; set; }

    public List<string> Rules { get; set; } = [];

    public List<string> SensitiveFieldsRemoved { get; set; } = [];
}

public sealed class InspectionEvidenceOutcomePolicy
{
    public bool Enabled { get; set; } = true;

    public string RetentionClass { get; set; } = "standard";

    public int RetentionDays { get; set; } = 30;

    public long MaxItemBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxItemsPerResult { get; set; } = 16;

    public bool CaptureOutputImage { get; set; } = true;

    public bool CaptureJsonEvidence { get; set; } = true;
}

public sealed class StudioEvidenceRetentionOptions
{
    public bool Enabled { get; set; } = true;

    public string? RootPath { get; set; }

    public long MaxTotalBytes { get; set; } = 1024L * 1024L * 1024L;

    public long MaxExportBytes { get; set; } = 64L * 1024L * 1024L;

    public InspectionEvidenceOutcomePolicy OK { get; set; } = new()
    {
        RetentionClass = "short",
        RetentionDays = 7,
        MaxItemBytes = 1024 * 1024,
        MaxItemsPerResult = 8,
        CaptureOutputImage = false,
        CaptureJsonEvidence = true
    };

    public InspectionEvidenceOutcomePolicy NG { get; set; } = new()
    {
        RetentionClass = "long",
        RetentionDays = 90,
        MaxItemBytes = 10 * 1024 * 1024,
        MaxItemsPerResult = 16,
        CaptureOutputImage = true,
        CaptureJsonEvidence = true
    };

    public InspectionEvidenceOutcomePolicy Error { get; set; } = new()
    {
        RetentionClass = "long",
        RetentionDays = 90,
        MaxItemBytes = 10 * 1024 * 1024,
        MaxItemsPerResult = 16,
        CaptureOutputImage = true,
        CaptureJsonEvidence = true
    };
}

public sealed class StationEvidenceRetentionOptions
{
    public bool Enabled { get; set; }

    public string? RootPath { get; set; }

    public long MaxTotalBytes { get; set; } = 512L * 1024L * 1024L;

    public long MaxExportBytes { get; set; } = 32L * 1024L * 1024L;

    public InspectionEvidenceOutcomePolicy OK { get; set; } = new()
    {
        RetentionClass = "station-short",
        RetentionDays = 3,
        MaxItemBytes = 512 * 1024,
        MaxItemsPerResult = 8,
        CaptureOutputImage = false,
        CaptureJsonEvidence = true
    };

    public InspectionEvidenceOutcomePolicy NG { get; set; } = new()
    {
        RetentionClass = "station-long",
        RetentionDays = 30,
        MaxItemBytes = 5 * 1024 * 1024,
        MaxItemsPerResult = 12,
        CaptureOutputImage = false,
        CaptureJsonEvidence = true
    };

    public InspectionEvidenceOutcomePolicy Error { get; set; } = new()
    {
        RetentionClass = "station-long",
        RetentionDays = 30,
        MaxItemBytes = 5 * 1024 * 1024,
        MaxItemsPerResult = 12,
        CaptureOutputImage = false,
        CaptureJsonEvidence = true
    };
}

public sealed class InspectionEvidenceSummary
{
    public bool HasEvidenceManifest { get; set; }

    public string EvidenceStatus { get; set; } = "missing";

    public string? EvidenceManifestReference { get; set; }

    public long? EvidenceTotalBytes { get; set; }

    public DateTimeOffset? RetentionExpiresAtUtc { get; set; }

    public string? RetentionClass { get; set; }

    public string? Message { get; set; }

    public string? Checksum { get; set; }
}

public sealed class InspectionEvidenceManifestReadResult
{
    public bool Found { get; set; }

    public string Status { get; set; } = "missing";

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }

    public InspectionEvidenceSummary Summary { get; set; } = new();

    public InspectionEvidenceManifestV1? Manifest { get; set; }

    public List<string> Warnings { get; set; } = [];
}

public sealed class InspectionEvidenceExportResult
{
    public bool Success { get; set; }

    public string Status { get; set; } = "missing";

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }

    public string FileName { get; set; } = "evidence-export.json";

    public string ContentType { get; set; } = "application/json";

    public byte[] Content { get; set; } = [];

    public long TotalBytes { get; set; }

    public string? Sha256 { get; set; }
}

public sealed class InspectionEvidenceRetentionCleanupResult
{
    public int DeletedManifestCount { get; set; }

    public int DeletedItemCount { get; set; }

    public long FreedBytes { get; set; }

    public List<string> DeletedResultIds { get; set; } = [];
}

public static class InspectionEvidenceRetentionPolicy
{
    public static InspectionEvidenceOutcomePolicy ForStatus(
        StudioEvidenceRetentionOptions options,
        InspectionStatus status)
    {
        return status switch
        {
            InspectionStatus.OK => options.OK,
            InspectionStatus.NG => options.NG,
            InspectionStatus.Error => options.Error,
            _ => options.Error
        };
    }
}
