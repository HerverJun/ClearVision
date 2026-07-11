namespace ClearVision.Product.Infrastructure.Data;

public sealed class StationNodeEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public string? LineName { get; set; }

    public string? AreaName { get; set; }

    public string? WorkcellName { get; set; }

    public string? InspectionNodeName { get; set; }

    public string? CameraAlias { get; set; }

    public string StationRole { get; set; } = string.Empty;

    public string? Owner { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public string? IpAddressHint { get; set; }

    public string? MacAddressHash { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public string OnlineState { get; set; } = "Unknown";

    public string RuntimeState { get; set; } = "Unknown";

    public string? CurrentPackageId { get; set; }

    public string? CurrentPackageName { get; set; }

    public string? CurrentPackageVersion { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string? Remark { get; set; }
}

public sealed class StationResultSummaryEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public long SequenceId { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string FlowHash { get; set; } = string.Empty;

    public string ImageId { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? InspectionStatus { get; set; }

    // Nullable canonical fields preserve the absence present in legacy Station payloads.
    public string? ExecutionOutcome { get; set; }

    public string? DecisionOutcome { get; set; }

    public bool? HasJudgmentSignal { get; set; }

    public string? DecisionSource { get; set; }

    public string? ReasonCode { get; set; }

    public long ExecutionTimeMs { get; set; }

    public string DiagnosticCode { get; set; } = string.Empty;

    public string? DiagnosticMessage { get; set; }

    public string PrimaryOutputsPreviewJson { get; set; } = "{}";

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class StationHealthSnapshotEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public long SequenceId { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public string RuntimeState { get; set; } = string.Empty;

    public long ProcessUptimeSeconds { get; set; }

    public double? CpuUsagePercent { get; set; }

    public long WorkingSetMb { get; set; }

    public long PrivateMemoryMb { get; set; }

    public long DiskFreeMb { get; set; }

    public long DiskTotalMb { get; set; }

    public int SpoolPendingCount { get; set; }

    public long SpoolBytes { get; set; }

    public string? CameraStatusSummary { get; set; }

    public string? PlcStatusSummary { get; set; }

    public string? CurrentPackageId { get; set; }

    public string? CurrentPackageHealth { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class StationConnectionEventEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string? Message { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class StationAlarmEventEntity
{
    public int Id { get; set; }

    public string AlarmId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class StationCommandRecordEntity
{
    public int Id { get; set; }

    public string CommandId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string CommandType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string Status { get; set; } = "Created";

    public int ProgressPercent { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? DeliveredAtUtc { get; set; }

    public DateTimeOffset? AcceptedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string IssuedBy { get; set; } = "Studio";

    public string CorrelationId { get; set; } = string.Empty;

    public string? ResultMessage { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }
}

public sealed class StationSyncCursorEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public long LastPersistedSequenceId { get; set; }

    public long LastReceivedHealthSequenceId { get; set; }

    public long LastReceivedLogSequenceId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class StationLogSummaryEntity
{
    public int Id { get; set; }

    public string StationId { get; set; } = string.Empty;

    public long SequenceId { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? EventId { get; set; }

    public string? MessageTemplate { get; set; }

    public string RenderedMessage { get; set; } = string.Empty;

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public string? CorrelationId { get; set; }

    public string? RunId { get; set; }

    public string? PackageId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class StationAuditRecordEntity
{
    public int Id { get; set; }

    public string AuditId { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? TargetStationId { get; set; }

    public string? CommandId { get; set; }

    public string? PayloadSummary { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? Result { get; set; }

    public string? ClientIp { get; set; }
}

public sealed class StationPackageRecordEntity
{
    public int Id { get; set; }

    public string PackageId { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string PackageKind { get; set; } = "Production";

    public string FlowHash { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = "Studio";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
