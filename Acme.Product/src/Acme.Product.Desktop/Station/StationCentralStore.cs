using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Station;

public sealed class StationCentralStore
{
    private static readonly TimeSpan CommandRedeliveryDelay = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: true)
        }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StationCentralStore> _logger;

    public StationCentralStore(
        IServiceScopeFactory scopeFactory,
        ILogger<StationCentralStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public long UpsertRegistration(StationRegistrationDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var node = db.StationNodes.FirstOrDefault(item => item.StationId == dto.StationId);
            if (node == null)
            {
                node = new StationNodeEntity
                {
                    StationId = dto.StationId,
                    FirstSeenAtUtc = now
                };
                db.StationNodes.Add(node);
                db.StationConnectionEvents.Add(new StationConnectionEventEntity
                {
                    StationId = dto.StationId,
                    EventType = "Registered",
                    Message = "Station registered with Studio.",
                    CreatedAtUtc = now
                });
            }

            node.StationName = Choose(dto.StationName, node.StationName);
            node.LineName = ChooseNullable(dto.LineName, node.LineName);
            node.AreaName = ChooseNullable(dto.AreaName, node.AreaName);
            node.WorkcellName = ChooseNullable(dto.WorkcellName, node.WorkcellName);
            node.InspectionNodeName = ChooseNullable(dto.InspectionNodeName, node.InspectionNodeName);
            node.CameraAlias = ChooseNullable(dto.CameraAlias, node.CameraAlias);
            node.StationRole = Choose(dto.StationRole, node.StationRole);
            node.Owner = ChooseNullable(dto.Owner, node.Owner);
            node.MachineName = dto.MachineName;
            node.IpAddressHint = ChooseNullable(dto.IpAddressHint, node.IpAddressHint);
            node.MacAddressHash = ChooseNullable(dto.MacAddressHash, node.MacAddressHash);
            node.LastSeenAtUtc = now;
            node.OnlineState = StationOnlineState.Online.ToString();
            node.CurrentPackageId = ChooseNullable(dto.CurrentPackageId, node.CurrentPackageId);
            node.CurrentPackageName = ChooseNullable(dto.CurrentPackageName, node.CurrentPackageName);
            node.CurrentPackageVersion = ChooseNullable(dto.CurrentPackageVersion, node.CurrentPackageVersion);

            var cursor = GetOrCreateCursor(db, dto.StationId);
            cursor.UpdatedAtUtc = now;
            db.SaveChanges();
            return cursor.LastPersistedSequenceId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station registration for {StationId}", dto.StationId);
            return 0;
        }
    }

    public long UpsertHeartbeat(StationHeartbeatDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var node = GetOrCreateNode(db, dto.StationId, now);
            node.LineName = ChooseNullable(dto.LineName, node.LineName);
            node.LastSeenAtUtc = now;
            node.LastHeartbeatAtUtc = dto.CreatedAtUtc == default ? now : dto.CreatedAtUtc;
            node.OnlineState = StationOnlineState.Online.ToString();
            node.RuntimeState = dto.RuntimeState.ToString();
            node.CurrentPackageId = ChooseNullable(dto.CurrentPackageId, node.CurrentPackageId);
            node.CurrentPackageName = ChooseNullable(dto.CurrentPackageName, node.CurrentPackageName);
            node.CurrentPackageVersion = ChooseNullable(dto.CurrentPackageVersion, node.CurrentPackageVersion);
            var cursor = GetOrCreateCursor(db, dto.StationId);
            cursor.UpdatedAtUtc = now;
            db.SaveChanges();
            return cursor.LastPersistedSequenceId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station heartbeat for {StationId}", dto.StationId);
            return 0;
        }
    }

    public StationAckDto UpsertResultSummary(StationResultSummaryDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var cursor = GetOrCreateCursor(db, dto.StationId);
            var duplicate = db.StationResultSummaries.Any(item =>
                item.StationId == dto.StationId &&
                item.SequenceId == dto.SequenceId);

            if (!duplicate)
            {
                db.StationResultSummaries.Add(ToEntity(dto, now));
                cursor.UpdatedAtUtc = now;

                var node = GetOrCreateNode(db, dto.StationId, now);
                node.LineName = ChooseNullable(dto.LineName, node.LineName);
                node.LastSeenAtUtc = now;
                node.OnlineState = StationOnlineState.Online.ToString();
                node.CurrentPackageId = ChooseNullable(dto.PackageId, node.CurrentPackageId);
                node.CurrentPackageName = ChooseNullable(dto.PackageName, node.CurrentPackageName);
                node.CurrentPackageVersion = ChooseNullable(dto.PackageVersion, node.CurrentPackageVersion);
            }

            db.SaveChanges();
            cursor.LastPersistedSequenceId = ComputeContiguousResultCursor(db, dto.StationId, cursor.LastPersistedSequenceId);
            cursor.UpdatedAtUtc = now;
            db.SaveChanges();
            return BuildAck(dto.StationId, dto.SequenceId, cursor.LastPersistedSequenceId, duplicate, duplicate ? "Duplicate result ignored." : "Result persisted.");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintFailure(ex))
        {
            var cursor = GetLastPersistedSequenceId(dto.StationId);
            return BuildAck(dto.StationId, dto.SequenceId, cursor, true, "Duplicate result ignored.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station result for {StationId}/{SequenceId}", dto.StationId, dto.SequenceId);
            return BuildAck(dto.StationId, dto.SequenceId, GetLastPersistedSequenceId(dto.StationId), false, "Result persistence failed.");
        }
    }

    public StationAckDto UpsertHealthSnapshot(StationHealthSnapshotDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var cursor = GetOrCreateCursor(db, dto.StationId);
            var duplicate = db.StationHealthSnapshots.Any(item =>
                item.StationId == dto.StationId &&
                item.SequenceId == dto.SequenceId);

            if (!duplicate)
            {
                db.StationHealthSnapshots.Add(ToEntity(dto, now));
                cursor.LastReceivedHealthSequenceId = Math.Max(cursor.LastReceivedHealthSequenceId, dto.SequenceId);
                cursor.UpdatedAtUtc = now;

                var node = GetOrCreateNode(db, dto.StationId, now);
                node.LastSeenAtUtc = now;
                node.RuntimeState = dto.RuntimeState.ToString();
                node.CurrentPackageId = ChooseNullable(dto.CurrentPackageId, node.CurrentPackageId);
                node.OnlineState = EvaluateOnlineState(dto).ToString();
            }

            db.SaveChanges();
            return BuildAck(dto.StationId, dto.SequenceId, cursor.LastPersistedSequenceId, duplicate, duplicate ? "Duplicate health ignored." : "Health persisted.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station health for {StationId}/{SequenceId}", dto.StationId, dto.SequenceId);
            return BuildAck(dto.StationId, dto.SequenceId, GetLastPersistedSequenceId(dto.StationId), false, "Health persistence failed.");
        }
    }

    public StationAckDto UpsertLogSummary(StationLogSummaryDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var cursor = GetOrCreateCursor(db, dto.StationId);
            var duplicate = db.StationLogSummaries.Any(item =>
                item.StationId == dto.StationId &&
                item.SequenceId == dto.SequenceId);

            if (!duplicate)
            {
                db.StationLogSummaries.Add(ToEntity(dto, now));
                cursor.LastReceivedLogSequenceId = Math.Max(cursor.LastReceivedLogSequenceId, dto.SequenceId);
                cursor.UpdatedAtUtc = now;
            }

            db.SaveChanges();
            return BuildAck(dto.StationId, dto.SequenceId, cursor.LastPersistedSequenceId, duplicate, duplicate ? "Duplicate log ignored." : "Log persisted.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station log for {StationId}/{SequenceId}", dto.StationId, dto.SequenceId);
            return BuildAck(dto.StationId, dto.SequenceId, GetLastPersistedSequenceId(dto.StationId), false, "Log persistence failed.");
        }
    }

    public long GetLastPersistedSequenceId(string stationId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            return db.StationSyncCursors
                .Where(item => item.StationId == stationId)
                .Select(item => item.LastPersistedSequenceId)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Station cursor for {StationId}", stationId);
            return 0;
        }
    }

    public IReadOnlyList<StationResultSummaryDto> GetRecentResults(string stationId, int take)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationResultSummaries
            .AsNoTracking()
            .Where(item => item.StationId == stationId)
            .OrderByDescending(item => item.SequenceId)
            .Take(Math.Clamp(take, 1, 500))
            .AsEnumerable()
            .Select(ToDto)
            .ToList();
    }

    public StationAckDto ReportResultGap(StationResultGapDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var droppedThrough = Math.Max(0, dto.DroppedThroughSequenceId);
            if (droppedThrough == 0)
            {
                return BuildAck(dto.StationId, 0, GetLastPersistedSequenceId(dto.StationId), true, "Empty result gap ignored.");
            }

            var droppedFrom = dto.DroppedFromSequenceId > 0
                ? Math.Min(dto.DroppedFromSequenceId, droppedThrough)
                : droppedThrough;
            var cursor = GetOrCreateCursor(db, dto.StationId);
            var previousCursor = cursor.LastPersistedSequenceId;
            var duplicate = droppedThrough <= previousCursor;
            if (!duplicate)
            {
                cursor.LastPersistedSequenceId = droppedThrough;
                cursor.UpdatedAtUtc = now;

                var node = GetOrCreateNode(db, dto.StationId, now);
                node.LastSeenAtUtc = now;
                node.OnlineState = StationOnlineState.Online.ToString();

                db.StationConnectionEvents.Add(new StationConnectionEventEntity
                {
                    StationId = dto.StationId,
                    EventType = "ResultGap",
                    Message = $"Station reported unavailable result sequence range {droppedFrom}-{droppedThrough}; Studio cursor advanced from {previousCursor} to {droppedThrough}.",
                    CreatedAtUtc = now
                });
            }

            db.SaveChanges();
            return BuildAck(
                dto.StationId,
                droppedThrough,
                Math.Max(previousCursor, droppedThrough),
                duplicate,
                duplicate ? "Result gap already acknowledged." : "Result gap acknowledged.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist Station result gap for {StationId}/{DroppedThroughSequenceId}",
                dto.StationId,
                dto.DroppedThroughSequenceId);
            return BuildAck(dto.StationId, dto.DroppedThroughSequenceId, GetLastPersistedSequenceId(dto.StationId), false, "Result gap persistence failed.");
        }
    }

    public StationResultsPageViewModel GetResultsPage(
        string? stationId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? status,
        string? diagnosticCode,
        int pageIndex,
        int pageSize)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var normalizedPageIndex = Math.Max(0, pageIndex);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
        var query = db.StationResultSummaries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            query = query.Where(item => item.StationId == stationId);
        }

        var filtered = query
            .AsEnumerable()
            .Where(item => !fromUtc.HasValue || item.CompletedAtUtc >= fromUtc.Value)
            .Where(item => !toUtc.HasValue || item.CompletedAtUtc <= toUtc.Value)
            .Where(item => MatchesStatus(item.Outcome, item.InspectionStatus, status))
            .Where(item => MatchesText(item.DiagnosticCode, diagnosticCode))
            .OrderByDescending(item => item.CompletedAtUtc)
            .ThenByDescending(item => item.SequenceId)
            .ToList();

        return new StationResultsPageViewModel
        {
            Items = filtered
                .Skip(normalizedPageIndex * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(ToDto)
                .ToList(),
            TotalCount = filtered.Count,
            PageIndex = normalizedPageIndex,
            PageSize = normalizedPageSize
        };
    }

    public IReadOnlyList<StationHealthSnapshotDto> GetRecentHealth(string stationId, int take)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationHealthSnapshots
            .AsNoTracking()
            .Where(item => item.StationId == stationId)
            .OrderByDescending(item => item.SequenceId)
            .Take(Math.Clamp(take, 1, 500))
            .AsEnumerable()
            .Select(ToDto)
            .ToList();
    }

    public IReadOnlyList<StationLogSummaryDto> GetRecentLogs(string stationId, int take)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationLogSummaries
            .AsNoTracking()
            .Where(item => item.StationId == stationId)
            .OrderByDescending(item => item.SequenceId)
            .Take(Math.Clamp(take, 1, 500))
            .AsEnumerable()
            .Select(ToDto)
            .ToList();
    }

    public void UpdateStationIdentity(
        string stationId,
        StationIdentityUpdateRequest request,
        string userName,
        string? clientIp)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var node = GetOrCreateNode(db, stationId, now);
        ApplyIdentityUpdate(node, request);
        node.LastSeenAtUtc = node.LastSeenAtUtc == default ? now : node.LastSeenAtUtc;
        AddAudit(db, new StationAuditDto
        {
            AuditId = $"audit_{Guid.NewGuid():N}",
            Action = "UpdateStationIdentity",
            TargetStationId = stationId,
            PayloadSummary = Redact(JsonSerializer.Serialize(request, JsonOptions)),
            Result = "Updated",
            UserName = string.IsNullOrWhiteSpace(userName) ? "Studio" : userName,
            ClientIp = clientIp,
            CreatedAtUtc = now
        });
        db.SaveChanges();
    }

    public IReadOnlyList<StationAuditViewModel> GetAudits(string? stationId, int take)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var query = db.StationAuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(stationId))
        {
            query = query.Where(item => item.TargetStationId == stationId);
        }

        return query
            .AsEnumerable()
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(item => new StationAuditViewModel
            {
                AuditId = item.AuditId,
                UserName = item.UserName,
                Action = item.Action,
                TargetStationId = item.TargetStationId,
                CommandId = item.CommandId,
                PayloadSummary = item.PayloadSummary,
                CreatedAtUtc = item.CreatedAtUtc,
                Result = item.Result,
                ClientIp = item.ClientIp
            })
            .ToList();
    }

    public StationCommandDto CreateCommand(
        string stationId,
        StationCommandType commandType,
        string payloadJson,
        string issuedBy,
        TimeSpan expiresIn)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var command = new StationCommandRecordEntity
        {
            CommandId = $"cmd_{Guid.NewGuid():N}",
            StationId = stationId,
            CommandType = commandType.ToString(),
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            Status = StationCommandStatus.Created.ToString(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(expiresIn <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : expiresIn),
            IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "Studio" : issuedBy,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        db.StationCommandRecords.Add(command);
        AddAudit(db, new StationAuditDto
        {
            AuditId = $"audit_{Guid.NewGuid():N}",
            Action = commandType.ToString(),
            TargetStationId = stationId,
            CommandId = command.CommandId,
            PayloadSummary = Redact(payloadJson),
            Result = "Created",
            UserName = command.IssuedBy,
            CreatedAtUtc = now
        });
        db.SaveChanges();
        return ToDto(command);
    }

    public StationCommandDto? PollCommand(string stationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var redeliverBeforeUtc = now - CommandRedeliveryDelay;
        var createdStatus = StationCommandStatus.Created.ToString();
        var deliveredStatus = StationCommandStatus.Delivered.ToString();

        var expired = db.StationCommandRecords
            .Where(item =>
                item.StationId == stationId &&
                (item.Status == createdStatus ||
                 item.Status == deliveredStatus))
            .AsEnumerable()
            .Where(item => item.ExpiresAtUtc <= now)
            .ToList();
        foreach (var item in expired)
        {
            item.Status = StationCommandStatus.TimedOut.ToString();
            item.CompletedAtUtc = now;
            item.ResultMessage = "Command expired before Station accepted it.";
        }

        var command = db.StationCommandRecords
            .Where(item =>
                item.StationId == stationId &&
                (item.Status == createdStatus ||
                 item.Status == deliveredStatus))
            .AsEnumerable()
            .Where(item => item.ExpiresAtUtc > now)
            .Where(item =>
                item.Status == createdStatus ||
                item.DeliveredAtUtc == null ||
                item.DeliveredAtUtc <= redeliverBeforeUtc)
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefault();

        if (command == null)
        {
            db.SaveChanges();
            return null;
        }

        command.Status = StationCommandStatus.Delivered.ToString();
        command.DeliveredAtUtc = now;
        db.SaveChanges();
        return ToDto(command);
    }

    public StationCommandDto? ReportCommandResult(StationCommandResultDto dto)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var command = db.StationCommandRecords.FirstOrDefault(item => item.CommandId == dto.CommandId);
        if (command == null)
        {
            return null;
        }

        var currentStatus = ParseCommandStatus(command.Status);
        if (!CanTransition(currentStatus, dto.Status))
        {
            AddAudit(db, new StationAuditDto
            {
                AuditId = $"audit_{Guid.NewGuid():N}",
                Action = "CommandTransitionRejected",
                TargetStationId = command.StationId,
                CommandId = command.CommandId,
                PayloadSummary = $"{currentStatus} -> {dto.Status}",
                Result = "Rejected",
                UserName = command.IssuedBy,
                CreatedAtUtc = now
            });
            db.SaveChanges();
            return ToDto(command);
        }

        var wasTerminal = IsTerminal(currentStatus);
        command.Status = dto.Status.ToString();
        command.ProgressPercent = dto.ProgressPercent;
        command.ResultMessage = dto.Message;
        command.ErrorCode = dto.ErrorCode;
        command.ErrorDetail = dto.ErrorDetail;
        command.StartedAtUtc = dto.StartedAtUtc ?? command.StartedAtUtc;
        command.CompletedAtUtc = dto.CompletedAtUtc ?? (IsTerminal(dto.Status) ? now : command.CompletedAtUtc);
        command.AcceptedAtUtc = dto.Status == StationCommandStatus.Accepted ? now : command.AcceptedAtUtc;
        if (dto.Status == StationCommandStatus.Running)
        {
            command.StartedAtUtc ??= now;
        }

        if (IsTerminal(dto.Status) && !wasTerminal)
        {
            AddAudit(db, new StationAuditDto
            {
                AuditId = $"audit_{Guid.NewGuid():N}",
                Action = "CommandCompleted",
                TargetStationId = command.StationId,
                CommandId = command.CommandId,
                PayloadSummary = Redact(command.PayloadJson),
                Result = dto.Status.ToString(),
                UserName = command.IssuedBy,
                CreatedAtUtc = now
            });
        }

        db.SaveChanges();
        return ToDto(command);
    }

    public IReadOnlyList<StationCommandDto> GetCommands(string stationId, int take)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        return db.StationCommandRecords
            .AsNoTracking()
            .Where(item => item.StationId == stationId)
            .AsEnumerable()
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(ToDto)
            .ToList();
    }

    public object GetStatistics(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        return GetStatistics(fromUtc, toUtc, null, null, null);
    }

    public object GetStatistics(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? stationId,
        string? status,
        string? diagnosticCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var query = db.StationResultSummaries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            query = query.Where(item => item.StationId == stationId);
        }

        var results = query
            .AsEnumerable()
            .Where(item => !fromUtc.HasValue || item.CompletedAtUtc >= fromUtc.Value)
            .Where(item => !toUtc.HasValue || item.CompletedAtUtc < toUtc.Value)
            .Where(item => MatchesStatus(item.Outcome, item.InspectionStatus, status))
            .Where(item => MatchesText(item.DiagnosticCode, diagnosticCode))
            .ToList();

        static bool IsOutcome(StationResultSummaryEntity item, string value)
        {
            return string.Equals(item.Outcome, value, StringComparison.OrdinalIgnoreCase);
        }

        var total = results.Count;
        var ok = results.Count(item => IsOutcome(item, "Ok"));
        var ng = results.Count(item => IsOutcome(item, "Ng"));
        var error = results.Count(item => IsOutcome(item, "Error") || IsOutcome(item, "Canceled"));
        var byDiagnosticCode = results
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DiagnosticCode) ? "Unknown" : item.DiagnosticCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { diagnosticCode = group.Key, defectType = group.Key, count = group.Count() })
            .OrderByDescending(item => item.count)
            .Take(20)
            .ToList();
        var hourlyTrend = results
            .GroupBy(item => new DateTimeOffset(item.CompletedAtUtc.Year, item.CompletedAtUtc.Month, item.CompletedAtUtc.Day, item.CompletedAtUtc.Hour, 0, 0, TimeSpan.Zero))
            .Select(group => new
            {
                hourUtc = group.Key,
                timestamp = group.Key,
                total = group.Count(),
                totalCount = group.Count(),
                ok = group.Count(item => IsOutcome(item, "Ok")),
                okCount = group.Count(item => IsOutcome(item, "Ok")),
                ng = group.Count(item => IsOutcome(item, "Ng")),
                ngCount = group.Count(item => IsOutcome(item, "Ng")),
                error = group.Count(item => IsOutcome(item, "Error") || IsOutcome(item, "Canceled")),
                errorCount = group.Count(item => IsOutcome(item, "Error") || IsOutcome(item, "Canceled")),
                defectCount = group.Count(item => IsOutcome(item, "Ng"))
            })
            .OrderBy(item => item.hourUtc)
            .ToList();

        return new
        {
            fromUtc,
            toUtc,
            total,
            totalCount = total,
            ok,
            okCount = ok,
            ng,
            ngCount = ng,
            error,
            errorCount = error,
            yieldRate = total == 0 ? 0 : Math.Round((double)ok / total, 4),
            okRate = total == 0 ? 0 : Math.Round((double)ok / total, 4),
            averageExecutionTimeMs = total == 0 ? 0 : results.Average(item => item.ExecutionTimeMs),
            averageProcessingTimeMs = total == 0 ? 0 : results.Average(item => item.ExecutionTimeMs),
            byStation = results
                .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    stationId = group.Key,
                    total = group.Count(),
                    totalCount = group.Count(),
                    ok = group.Count(item => IsOutcome(item, "Ok")),
                    okCount = group.Count(item => IsOutcome(item, "Ok")),
                    ng = group.Count(item => IsOutcome(item, "Ng")),
                    ngCount = group.Count(item => IsOutcome(item, "Ng")),
                    error = group.Count(item => IsOutcome(item, "Error") || IsOutcome(item, "Canceled")),
                    errorCount = group.Count(item => IsOutcome(item, "Error") || IsOutcome(item, "Canceled")),
                    averageExecutionTimeMs = group.Average(item => item.ExecutionTimeMs)
                })
                .OrderByDescending(item => item.total)
                .ToList(),
            byDiagnosticCode,
            defectDistribution = new { items = byDiagnosticCode },
            hourlyTrend,
            trend = new { dataPoints = hourlyTrend }
        };
    }

    private static StationNodeEntity GetOrCreateNode(VisionDbContext db, string stationId, DateTimeOffset now)
    {
        var node = db.StationNodes.FirstOrDefault(item => item.StationId == stationId);
        if (node != null)
        {
            return node;
        }

        node = new StationNodeEntity
        {
            StationId = stationId,
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now,
            OnlineState = StationOnlineState.Unknown.ToString(),
            RuntimeState = StationRuntimeState.Unknown.ToString()
        };
        db.StationNodes.Add(node);
        return node;
    }

    private static StationSyncCursorEntity GetOrCreateCursor(VisionDbContext db, string stationId)
    {
        var cursor = db.StationSyncCursors.FirstOrDefault(item => item.StationId == stationId);
        if (cursor != null)
        {
            return cursor;
        }

        cursor = new StationSyncCursorEntity
        {
            StationId = stationId,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.StationSyncCursors.Add(cursor);
        return cursor;
    }

    private static long ComputeContiguousResultCursor(
        VisionDbContext db,
        string stationId,
        long currentCursor)
    {
        var sequences = db.StationResultSummaries
            .AsNoTracking()
            .Where(item => item.StationId == stationId && item.SequenceId > currentCursor)
            .OrderBy(item => item.SequenceId)
            .Select(item => item.SequenceId)
            .ToList();

        if (currentCursor == 0 && sequences.Count > 0)
        {
            currentCursor = sequences[0];
            sequences.RemoveAt(0);
        }

        foreach (var sequence in sequences)
        {
            if (sequence == currentCursor)
            {
                continue;
            }

            if (sequence != currentCursor + 1)
            {
                break;
            }

            currentCursor = sequence;
        }

        return currentCursor;
    }

    private static StationResultSummaryEntity ToEntity(StationResultSummaryDto dto, DateTimeOffset receivedAtUtc)
    {
        return new StationResultSummaryEntity
        {
            StationId = dto.StationId,
            SequenceId = dto.SequenceId,
            MessageId = EnsureMessageId(dto.MessageId),
            RunId = dto.RunId,
            PackageId = dto.PackageId,
            PackageName = dto.PackageName,
            PackageVersion = dto.PackageVersion,
            FlowHash = dto.FlowHash,
            ImageId = dto.ImageId,
            Outcome = dto.Outcome.ToString(),
            InspectionStatus = dto.InspectionStatus?.ToString(),
            ExecutionTimeMs = dto.ExecutionTimeMs,
            DiagnosticCode = dto.DiagnosticCode,
            DiagnosticMessage = dto.DiagnosticMessage,
            PrimaryOutputsPreviewJson = JsonSerializer.Serialize(dto.PrimaryOutputsPreview ?? [], JsonOptions),
            StartedAtUtc = dto.StartedAtUtc,
            CompletedAtUtc = dto.CompletedAtUtc,
            CreatedAtUtc = dto.CreatedAtUtc == default ? receivedAtUtc : dto.CreatedAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    private static StationHealthSnapshotEntity ToEntity(StationHealthSnapshotDto dto, DateTimeOffset receivedAtUtc)
    {
        return new StationHealthSnapshotEntity
        {
            StationId = dto.StationId,
            SequenceId = dto.SequenceId,
            MessageId = EnsureMessageId(dto.MessageId),
            RuntimeState = dto.RuntimeState.ToString(),
            ProcessUptimeSeconds = dto.ProcessUptimeSeconds,
            CpuUsagePercent = dto.CpuUsagePercent,
            WorkingSetMb = dto.WorkingSetMb,
            PrivateMemoryMb = dto.PrivateMemoryMb,
            DiskFreeMb = dto.DiskFreeMb,
            DiskTotalMb = dto.DiskTotalMb,
            SpoolPendingCount = dto.SpoolPendingCount,
            SpoolBytes = dto.SpoolBytes,
            CameraStatusSummary = dto.CameraStatusSummary,
            PlcStatusSummary = dto.PlcStatusSummary,
            CurrentPackageId = dto.CurrentPackageId,
            CurrentPackageHealth = dto.CurrentPackageHealth,
            LastErrorCode = dto.LastErrorCode,
            LastErrorMessage = dto.LastErrorMessage,
            CreatedAtUtc = dto.CreatedAtUtc == default ? receivedAtUtc : dto.CreatedAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    private static StationLogSummaryEntity ToEntity(StationLogSummaryDto dto, DateTimeOffset receivedAtUtc)
    {
        return new StationLogSummaryEntity
        {
            StationId = dto.StationId,
            SequenceId = dto.SequenceId,
            MessageId = EnsureMessageId(dto.MessageId),
            TimestampUtc = dto.TimestampUtc,
            Level = dto.Level,
            Source = dto.Source,
            EventId = dto.EventId,
            MessageTemplate = dto.MessageTemplate,
            RenderedMessage = Truncate(dto.RenderedMessage, 4000) ?? string.Empty,
            ExceptionType = dto.ExceptionType,
            ExceptionMessage = Truncate(dto.ExceptionMessage, 2000),
            CorrelationId = dto.CorrelationId,
            RunId = dto.RunId,
            PackageId = dto.PackageId,
            CreatedAtUtc = dto.CreatedAtUtc == default ? receivedAtUtc : dto.CreatedAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    private static StationResultSummaryDto ToDto(StationResultSummaryEntity entity)
    {
        return new StationResultSummaryDto
        {
            StationId = entity.StationId,
            SequenceId = entity.SequenceId,
            MessageId = entity.MessageId,
            RunId = entity.RunId,
            PackageId = entity.PackageId,
            PackageName = entity.PackageName,
            PackageVersion = entity.PackageVersion,
            FlowHash = entity.FlowHash,
            ImageId = entity.ImageId,
            Outcome = Enum.TryParse<RuntimeRunOutcome>(entity.Outcome, true, out var outcome) ? outcome : RuntimeRunOutcome.Error,
            InspectionStatus = Enum.TryParse<Acme.Product.Core.Enums.InspectionStatus>(entity.InspectionStatus, true, out var status) ? status : null,
            ExecutionTimeMs = entity.ExecutionTimeMs,
            DiagnosticCode = entity.DiagnosticCode,
            DiagnosticMessage = entity.DiagnosticMessage,
            PrimaryOutputsPreview = DeserializePreview(entity.PrimaryOutputsPreviewJson),
            StartedAtUtc = entity.StartedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static StationHealthSnapshotDto ToDto(StationHealthSnapshotEntity entity)
    {
        return new StationHealthSnapshotDto
        {
            StationId = entity.StationId,
            SequenceId = entity.SequenceId,
            MessageId = entity.MessageId,
            RuntimeState = Enum.TryParse<StationRuntimeState>(entity.RuntimeState, true, out var state) ? state : StationRuntimeState.Unknown,
            ProcessUptimeSeconds = entity.ProcessUptimeSeconds,
            CpuUsagePercent = entity.CpuUsagePercent,
            WorkingSetMb = entity.WorkingSetMb,
            PrivateMemoryMb = entity.PrivateMemoryMb,
            DiskFreeMb = entity.DiskFreeMb,
            DiskTotalMb = entity.DiskTotalMb,
            SpoolPendingCount = entity.SpoolPendingCount,
            SpoolBytes = entity.SpoolBytes,
            CameraStatusSummary = entity.CameraStatusSummary,
            PlcStatusSummary = entity.PlcStatusSummary,
            CurrentPackageId = entity.CurrentPackageId,
            CurrentPackageHealth = entity.CurrentPackageHealth,
            LastErrorCode = entity.LastErrorCode,
            LastErrorMessage = entity.LastErrorMessage,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static StationLogSummaryDto ToDto(StationLogSummaryEntity entity)
    {
        return new StationLogSummaryDto
        {
            StationId = entity.StationId,
            SequenceId = entity.SequenceId,
            MessageId = entity.MessageId,
            TimestampUtc = entity.TimestampUtc,
            Level = entity.Level,
            Source = entity.Source,
            EventId = entity.EventId,
            MessageTemplate = entity.MessageTemplate,
            RenderedMessage = entity.RenderedMessage,
            ExceptionType = entity.ExceptionType,
            ExceptionMessage = entity.ExceptionMessage,
            CorrelationId = entity.CorrelationId,
            RunId = entity.RunId,
            PackageId = entity.PackageId,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static StationCommandDto ToDto(StationCommandRecordEntity entity)
    {
        return new StationCommandDto
        {
            CommandId = entity.CommandId,
            StationId = entity.StationId,
            CommandType = Enum.TryParse<StationCommandType>(entity.CommandType, true, out var type) ? type : StationCommandType.Ping,
            PayloadJson = entity.PayloadJson,
            CreatedAtUtc = entity.CreatedAtUtc,
            ExpiresAtUtc = entity.ExpiresAtUtc,
            IssuedBy = entity.IssuedBy,
            CorrelationId = entity.CorrelationId,
            Status = ParseCommandStatus(entity.Status),
            ProgressPercent = entity.ProgressPercent,
            DeliveredAtUtc = entity.DeliveredAtUtc,
            AcceptedAtUtc = entity.AcceptedAtUtc,
            StartedAtUtc = entity.StartedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
            ResultMessage = entity.ResultMessage,
            ErrorCode = entity.ErrorCode
        };
    }

    private static void AddAudit(VisionDbContext db, StationAuditDto audit)
    {
        db.StationAuditRecords.Add(new StationAuditRecordEntity
        {
            AuditId = string.IsNullOrWhiteSpace(audit.AuditId) ? $"audit_{Guid.NewGuid():N}" : audit.AuditId,
            UserId = audit.UserId,
            UserName = audit.UserName,
            Action = audit.Action,
            TargetStationId = audit.TargetStationId,
            CommandId = audit.CommandId,
            PayloadSummary = audit.PayloadSummary,
            CreatedAtUtc = audit.CreatedAtUtc == default ? DateTimeOffset.UtcNow : audit.CreatedAtUtc,
            Result = audit.Result,
            ClientIp = audit.ClientIp
        });
    }

    private static StationOnlineState EvaluateOnlineState(StationHealthSnapshotDto dto)
    {
        if (dto.RuntimeState == StationRuntimeState.Faulted)
        {
            return StationOnlineState.Critical;
        }

        if (dto.DiskTotalMb > 0)
        {
            var freeRatio = (double)dto.DiskFreeMb / dto.DiskTotalMb;
            if (freeRatio < 0.05d)
            {
                return StationOnlineState.Critical;
            }

            if (freeRatio < 0.10d)
            {
                return StationOnlineState.Warning;
            }
        }

        if (dto.SpoolPendingCount > 10_000 ||
            (dto.CameraStatusSummary?.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return StationOnlineState.Critical;
        }

        if (dto.SpoolPendingCount > 1_000)
        {
            return StationOnlineState.Warning;
        }

        return StationOnlineState.Online;
    }

    private static void ApplyIdentityUpdate(StationNodeEntity node, StationIdentityUpdateRequest request)
    {
        node.StationName = Choose(request.StationName, node.StationName);
        node.LineName = ChooseNullable(request.LineName, node.LineName);
        node.AreaName = ChooseNullable(request.AreaName, node.AreaName);
        node.WorkcellName = ChooseNullable(request.WorkcellName, node.WorkcellName);
        node.InspectionNodeName = ChooseNullable(request.InspectionNodeName, node.InspectionNodeName);
        node.CameraAlias = ChooseNullable(request.CameraAlias, node.CameraAlias);
        node.StationRole = Choose(request.StationRole, node.StationRole);
        node.Owner = ChooseNullable(request.Owner, node.Owner);
        node.Remark = ChooseNullable(request.Remark, node.Remark);
        if (request.IsEnabled.HasValue)
        {
            node.IsEnabled = request.IsEnabled.Value;
        }
    }

    private static StationAckDto BuildAck(string stationId, long acceptedSequenceId, long lastPersistedSequenceId, bool duplicate, string? message)
    {
        return new StationAckDto
        {
            StationId = stationId,
            AcceptedSequenceId = acceptedSequenceId,
            LastPersistedSequenceId = lastPersistedSequenceId,
            Duplicate = duplicate,
            Message = message,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool IsUniqueConstraintFailure(DbUpdateException exception)
    {
        return exception.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsTerminal(StationCommandStatus status)
    {
        return status is StationCommandStatus.Succeeded
            or StationCommandStatus.Failed
            or StationCommandStatus.TimedOut
            or StationCommandStatus.Cancelled
            or StationCommandStatus.Rejected;
    }

    private static StationCommandStatus ParseCommandStatus(string status)
    {
        return Enum.TryParse<StationCommandStatus>(status, true, out var parsed)
            ? parsed
            : StationCommandStatus.Created;
    }

    private static bool CanTransition(StationCommandStatus current, StationCommandStatus next)
    {
        if (current == next)
        {
            return true;
        }

        if (IsTerminal(current))
        {
            return false;
        }

        return current switch
        {
            StationCommandStatus.Created => next is StationCommandStatus.Delivered or StationCommandStatus.Cancelled or StationCommandStatus.TimedOut,
            StationCommandStatus.Delivered => next is StationCommandStatus.Accepted or StationCommandStatus.Rejected or StationCommandStatus.Running or StationCommandStatus.Failed or StationCommandStatus.Cancelled or StationCommandStatus.TimedOut,
            StationCommandStatus.Accepted => next is StationCommandStatus.Running or StationCommandStatus.Succeeded or StationCommandStatus.Failed or StationCommandStatus.Rejected or StationCommandStatus.Cancelled,
            StationCommandStatus.Running => next is StationCommandStatus.Running or StationCommandStatus.Succeeded or StationCommandStatus.Failed or StationCommandStatus.Cancelled,
            _ => false
        };
    }

    private static string Choose(string? candidate, string existing)
    {
        return string.IsNullOrWhiteSpace(candidate) ? existing : candidate.Trim();
    }

    private static string? ChooseNullable(string? candidate, string? existing)
    {
        return string.IsNullOrWhiteSpace(candidate) ? existing : candidate.Trim();
    }

    private static string EnsureMessageId(string messageId)
    {
        return string.IsNullOrWhiteSpace(messageId) ? $"msg_{Guid.NewGuid():N}" : messageId.Trim();
    }

    private static Dictionary<string, string?> DeserializePreview(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Redact(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "{}";
        }

        try
        {
            var node = JsonNode.Parse(payload);
            if (node != null)
            {
                RedactNode(node);
                return Truncate(node.ToJsonString(JsonOptions), 1000) ?? "{}";
            }
        }
        catch
        {
        }

        return ContainsSensitiveKey(payload)
            ? "[redacted-payload]"
            : Truncate(payload, 1000) ?? "{}";
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (ContainsSensitiveKey(property.Key))
                {
                    jsonObject[property.Key] = "[redacted]";
                }
                else if (property.Value != null)
                {
                    RedactNode(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item != null)
                {
                    RedactNode(item);
                }
            }
        }
    }

    private static bool ContainsSensitiveKey(string value)
    {
        return value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apiKey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("authorization", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesStatus(string? outcome, string? inspectionStatus, string? requestedStatus)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus) ||
            string.Equals(requestedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(outcome, requestedStatus, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(inspectionStatus, requestedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesText(string? value, string? requestedValue)
    {
        return string.IsNullOrWhiteSpace(requestedValue) ||
               string.Equals(requestedValue, "all", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, requestedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }
}
