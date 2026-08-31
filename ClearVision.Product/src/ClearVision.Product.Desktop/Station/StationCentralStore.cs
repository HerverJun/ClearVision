using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Data;
using System.Data.Common;
using System.Globalization;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Station;

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
    private readonly object _commandResultSync = new();

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

    public long UpsertSnapshot(StationSnapshotDto dto)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var now = DateTimeOffset.UtcNow;
            var node = GetOrCreateNode(db, dto.StationId, now);
            node.LineName = ChooseNullable(dto.LineName, node.LineName);
            node.LastSeenAtUtc = now;
            node.OnlineState = StationOnlineState.Online.ToString();
            node.RuntimeState = dto.RuntimeState.ToString();
            node.CurrentPackageId = dto.CurrentPackageId;
            node.CurrentPackageName = dto.CurrentPackageName;
            node.CurrentPackageVersion = dto.CurrentPackageVersion;
            ApplyExecutionIdentity(node, dto.PackageFlowHash, dto.ExecutionFlowHash, dto.FlowHash,
                dto.ExecutionSnapshotId, dto.ProjectRevision, dto.DecisionConfigurationHash,
                dto.ExecutionRunMode, dto.CurrentRunId);
            var cursor = GetOrCreateCursor(db, dto.StationId);
            cursor.UpdatedAtUtc = now;
            db.SaveChanges();
            return cursor.LastPersistedSequenceId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Station snapshot for {StationId}", dto.StationId);
            return 0;
        }
    }

    public IReadOnlyList<StationStatusViewModel> GetStationStatuses()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            return db.StationNodes.AsNoTracking().OrderBy(item => item.StationId).AsEnumerable().Select(node => new StationStatusViewModel
            {
                StationId = node.StationId,
                StationName = node.StationName,
                LineName = node.LineName,
                AreaName = node.AreaName,
                WorkcellName = node.WorkcellName,
                InspectionNodeName = node.InspectionNodeName,
                CameraAlias = node.CameraAlias,
                StationRole = node.StationRole,
                Owner = node.Owner,
                MachineName = node.MachineName,
                IsEnabled = node.IsEnabled,
                Remark = node.Remark,
                OnlineState = Enum.TryParse<StationOnlineState>(node.OnlineState, true, out var online) ? online : StationOnlineState.Unknown,
                RuntimeState = Enum.TryParse<StationRuntimeState>(node.RuntimeState, true, out var runtime) ? runtime : StationRuntimeState.Unknown,
                State = StationSyncStateMapper.ToRuntimeHostState(Enum.TryParse<StationRuntimeState>(node.RuntimeState, true, out var state) ? state : StationRuntimeState.Unknown),
                StartedAtUtc = node.FirstSeenAtUtc,
                LastSeenAtUtc = node.LastSeenAtUtc,
                PackageId = node.CurrentPackageId,
                PackageName = node.CurrentPackageName,
                PackageFlowHash = node.PackageFlowHash,
                ExecutionFlowHash = node.ExecutionFlowHash,
                FlowHash = node.ExecutionFlowHash ?? node.FlowHash,
                ExecutionSnapshotId = node.ExecutionSnapshotId,
                ProjectRevision = node.ProjectRevision,
                DecisionConfigurationHash = node.DecisionConfigurationHash,
                ExecutionRunMode = node.ExecutionRunMode,
                CurrentRunId = node.CurrentRunId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload Station registry status.");
            return Array.Empty<StationStatusViewModel>();
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
            ApplyExecutionIdentity(node, dto.PackageFlowHash, dto.ExecutionFlowHash, dto.FlowHash,
                dto.ExecutionSnapshotId, dto.ProjectRevision, dto.DecisionConfigurationHash,
                dto.ExecutionRunMode, dto.CurrentRunId);
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
                ApplyExecutionIdentity(node, dto.PackageFlowHash, dto.ExecutionFlowHash, dto.FlowHash,
                    dto.ExecutionSnapshotId, dto.ProjectRevision, dto.DecisionConfigurationHash,
                    dto.ExecutionRunMode, dto.RunId);
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
        var window = StationResultQueryBudget.Normalize(fromUtc, toUtc, DateTimeOffset.UtcNow);
        var normalizedPageIndex = Math.Max(0, pageIndex);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
        var filter = BuildResultFilter(
            stationId,
            window,
            status,
            diagnosticCode);
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            connection.Open();
        }

        try
        {
            var totalCount = ExecuteResultCount(connection, filter);
            var items = ExecuteResultPage(
                connection,
                filter,
                normalizedPageIndex,
                normalizedPageSize)
                .Select(ToDto)
                .ToList();

            return new StationResultsPageViewModel
            {
                Items = items,
                TotalCount = totalCount,
                PageIndex = normalizedPageIndex,
                PageSize = normalizedPageSize
            };
        }
        finally
        {
            if (closeConnection)
            {
                connection.Close();
            }
        }
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

    public StationCommandDto? ReportCommandResult(
        string authenticatedStationId,
        StationCommandResultDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var normalizedStationId = authenticatedStationId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStationId) ||
            string.IsNullOrWhiteSpace(dto.CommandId) ||
            !string.Equals(dto.StationId?.Trim(), normalizedStationId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        lock (_commandResultSync)
        {
            return ReportCommandResultLocked(normalizedStationId, dto);
        }
    }

    private StationCommandDto? ReportCommandResultLocked(
        string authenticatedStationId,
        StationCommandResultDto dto)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var command = db.StationCommandRecords.FirstOrDefault(item =>
            item.CommandId == dto.CommandId &&
            item.StationId == authenticatedStationId);
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

    public StationResultStatisticsViewModel GetStatistics(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        return GetStatistics(fromUtc, toUtc, null, null, null);
    }

    public StationResultStatisticsViewModel GetStatistics(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? stationId,
        string? status,
        string? diagnosticCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var window = StationResultQueryBudget.Normalize(fromUtc, toUtc, DateTimeOffset.UtcNow);
        var filter = BuildResultFilter(stationId, window, status, diagnosticCode);
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            connection.Open();
        }

        try
        {
            var overall = ExecuteOutcomeAggregates(connection, filter, includeStationId: false);
            var byStation = ExecuteOutcomeAggregates(connection, filter, includeStationId: true);
            var diagnostics = ExecuteDiagnosticAggregates(connection, filter);
            var hourly = ExecuteHourlyAggregates(connection, filter);
            return BuildStatistics(window, overall, byStation, diagnostics, hourly);
        }
        finally
        {
            if (closeConnection)
            {
                connection.Close();
            }
        }
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
            PackageFlowHash = dto.PackageFlowHash,
            ExecutionFlowHash = dto.ExecutionFlowHash,
            ExecutionSnapshotId = dto.ExecutionSnapshotId,
            ProjectRevision = dto.ProjectRevision,
            DecisionConfigurationHash = dto.DecisionConfigurationHash,
            ExecutionRunMode = dto.ExecutionRunMode,
            ImageId = dto.ImageId,
            Outcome = dto.Outcome.ToString(),
            InspectionStatus = dto.InspectionStatus?.ToString(),
            ExecutionOutcome = dto.ExecutionOutcome?.ToString(),
            DecisionOutcome = dto.DecisionOutcome?.ToString(),
            HasJudgmentSignal = dto.HasJudgmentSignal,
            DecisionSource = dto.DecisionSource,
            ReasonCode = dto.ReasonCode,
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
            PackageFlowHash = entity.PackageFlowHash ?? string.Empty,
            ExecutionFlowHash = entity.ExecutionFlowHash ?? entity.FlowHash,
            ExecutionSnapshotId = entity.ExecutionSnapshotId,
            ProjectRevision = entity.ProjectRevision ?? 0,
            DecisionConfigurationHash = entity.DecisionConfigurationHash,
            ExecutionRunMode = entity.ExecutionRunMode,
            ImageId = entity.ImageId,
            Outcome = Enum.TryParse<RuntimeRunOutcome>(entity.Outcome, true, out var outcome) ? outcome : RuntimeRunOutcome.Error,
            InspectionStatus = Enum.TryParse<ClearVision.Product.Core.Enums.InspectionStatus>(entity.InspectionStatus, true, out var status) ? status : null,
            ExecutionOutcome = Enum.TryParse<ClearVision.Product.Core.Outcomes.ExecutionOutcome>(entity.ExecutionOutcome, true, out var executionOutcome) ? executionOutcome : null,
            DecisionOutcome = Enum.TryParse<ClearVision.Product.Core.Outcomes.DecisionOutcome>(entity.DecisionOutcome, true, out var decisionOutcome) ? decisionOutcome : null,
            HasJudgmentSignal = entity.HasJudgmentSignal,
            DecisionSource = entity.DecisionSource,
            ReasonCode = entity.ReasonCode,
            ExecutionTimeMs = entity.ExecutionTimeMs,
            DiagnosticCode = entity.DiagnosticCode,
            DiagnosticMessage = entity.DiagnosticMessage,
            PrimaryOutputsPreview = DeserializePreview(entity.PrimaryOutputsPreviewJson),
            StartedAtUtc = entity.StartedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static void ApplyExecutionIdentity(
        StationNodeEntity node,
        string? packageFlowHash,
        string? executionFlowHash,
        string? legacyFlowHash,
        Guid? executionSnapshotId,
        long? projectRevision,
        string? decisionConfigurationHash,
        string? executionRunMode,
        string? currentRunId)
    {
        var canonicalExecutionHash = string.IsNullOrWhiteSpace(executionFlowHash) ? legacyFlowHash : executionFlowHash;
        node.PackageFlowHash = string.IsNullOrWhiteSpace(packageFlowHash) ? null : packageFlowHash;
        node.ExecutionFlowHash = string.IsNullOrWhiteSpace(canonicalExecutionHash) ? null : canonicalExecutionHash;
        node.FlowHash = node.ExecutionFlowHash;
        node.ExecutionSnapshotId = executionSnapshotId;
        node.ProjectRevision = projectRevision;
        node.DecisionConfigurationHash = string.IsNullOrWhiteSpace(decisionConfigurationHash) ? null : decisionConfigurationHash;
        node.ExecutionRunMode = string.IsNullOrWhiteSpace(executionRunMode) ? null : executionRunMode;
        node.CurrentRunId = string.IsNullOrWhiteSpace(currentRunId) ? null : currentRunId;
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

    private static StationResultSqlFilter BuildResultFilter(
        string? stationId,
        StationResultWindow window,
        string? status,
        string? diagnosticCode)
    {
        var predicates = new List<string>
        {
            "julianday(\"CompletedAtUtc\") >= julianday($fromUtc)",
            "julianday(\"CompletedAtUtc\") <= julianday($toUtc)"
        };
        var parameters = new List<StationResultSqlParameter>
        {
            new("$fromUtc", window.FromUtc.ToString("O", CultureInfo.InvariantCulture)),
            new("$toUtc", window.ToUtc.ToString("O", CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            predicates.Add("\"StationId\" = $stationId");
            parameters.Add(new StationResultSqlParameter("$stationId", stationId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(diagnosticCode) &&
            !string.Equals(diagnosticCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            predicates.Add("UPPER(\"DiagnosticCode\") = $diagnosticCode");
            parameters.Add(new StationResultSqlParameter("$diagnosticCode", diagnosticCode.Trim().ToUpperInvariant()));
        }

        var statusPredicate = BuildStatusPredicate(status);
        if (!string.IsNullOrEmpty(statusPredicate))
        {
            predicates.Add(statusPredicate);
        }

        return new StationResultSqlFilter(
            " WHERE " + string.Join(" AND ", predicates),
            parameters);
    }

    private static string? BuildStatusPredicate(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        const string legacy = "(\"ExecutionOutcome\" IS NULL OR \"DecisionOutcome\" IS NULL)";
        var token = new string(status.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return token switch
        {
            "ok" => "((\"ExecutionOutcome\" = 'Succeeded' AND \"DecisionOutcome\" = 'Ok') OR (" + legacy + " AND \"Outcome\" = 'Ok'))",
            "ng" => "((\"ExecutionOutcome\" = 'Succeeded' AND \"DecisionOutcome\" = 'Ng') OR (" + legacy + " AND \"Outcome\" = 'Ng'))",
            "undetermined" => "((\"ExecutionOutcome\" = 'Succeeded' AND \"DecisionOutcome\" = 'Undetermined') OR (" + legacy + " AND \"Outcome\" = 'Undetermined'))",
            "notapplicable" => "(\"ExecutionOutcome\" = 'Succeeded' AND \"DecisionOutcome\" = 'NotApplicable')",
            "invalid" => "(\"ExecutionOutcome\" = 'Succeeded' AND \"DecisionOutcome\" = 'Invalid')",
            "failed" => "(\"ExecutionOutcome\" = 'Failed' OR (" + legacy + " AND \"Outcome\" = 'Error'))",
            "cancelled" or "canceled" => "(\"ExecutionOutcome\" = 'Cancelled' OR (" + legacy + " AND \"Outcome\" = 'Canceled'))",
            "timedout" => "(\"ExecutionOutcome\" = 'TimedOut')",
            "skipped" => "(\"ExecutionOutcome\" = 'Skipped')",
            "error" => "(\"ExecutionOutcome\" IN ('Failed', 'TimedOut') OR (" + legacy + " AND \"Outcome\" = 'Error'))",
            _ => null
        };
    }

    private static int ExecuteResultCount(DbConnection connection, StationResultSqlFilter filter)
    {
        using var command = CreateResultCommand(
            connection,
            "SELECT COUNT(*) FROM \"StationResultSummaries\"" + filter.WhereClause,
            filter);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<StationResultSummaryEntity> ExecuteResultPage(
        DbConnection connection,
        StationResultSqlFilter filter,
        int pageIndex,
        int pageSize)
    {
        using var command = CreateResultCommand(
            connection,
            "SELECT * FROM \"StationResultSummaries\"" + filter.WhereClause +
            " ORDER BY julianday(\"CompletedAtUtc\") DESC, \"SequenceId\" DESC LIMIT $pageSize OFFSET $pageOffset",
            filter,
            new StationResultSqlParameter("$pageSize", pageSize),
            new StationResultSqlParameter("$pageOffset", checked(pageIndex * pageSize)));
        using var reader = command.ExecuteReader();
        var results = new List<StationResultSummaryEntity>();
        while (reader.Read())
        {
            results.Add(ReadResultEntity(reader));
        }

        return results;
    }

    private static IReadOnlyList<StationOutcomeAggregate> ExecuteOutcomeAggregates(
        DbConnection connection,
        StationResultSqlFilter filter,
        bool includeStationId)
    {
        const string outcomeColumns = "\"Outcome\", \"InspectionStatus\", \"ExecutionOutcome\", \"DecisionOutcome\", \"HasJudgmentSignal\"";
        var stationColumn = includeStationId ? "\"StationId\", " : string.Empty;
        var groupBy = includeStationId ? "\"StationId\", " + outcomeColumns : outcomeColumns;
        using var command = CreateResultCommand(
            connection,
            "SELECT " + stationColumn + outcomeColumns +
            ", COUNT(*) AS \"Count\", COALESCE(SUM(\"ExecutionTimeMs\"), 0) AS \"ExecutionTimeTotal\" FROM \"StationResultSummaries\"" +
            filter.WhereClause +
            " GROUP BY " + groupBy,
            filter);
        using var reader = command.ExecuteReader();
        var results = new List<StationOutcomeAggregate>();
        while (reader.Read())
        {
            results.Add(new StationOutcomeAggregate(
                includeStationId ? ReadRequiredString(reader, "StationId") : string.Empty,
                ReadRequiredString(reader, "Outcome"),
                ReadNullableString(reader, "InspectionStatus"),
                ReadNullableString(reader, "ExecutionOutcome"),
                ReadNullableString(reader, "DecisionOutcome"),
                ReadNullableBoolean(reader, "HasJudgmentSignal"),
                ReadInt32(reader, "Count"),
                ReadDouble(reader, "ExecutionTimeTotal")));
        }

        return results;
    }

    private static IReadOnlyList<StationDiagnosticAggregate> ExecuteDiagnosticAggregates(
        DbConnection connection,
        StationResultSqlFilter filter)
    {
        const string diagnosticExpression = "CASE WHEN \"DiagnosticCode\" IS NULL OR TRIM(\"DiagnosticCode\") = '' THEN 'Unknown' ELSE \"DiagnosticCode\" END";
        using var command = CreateResultCommand(
            connection,
            "SELECT " + diagnosticExpression + " AS \"DiagnosticCode\", COUNT(*) AS \"Count\" FROM \"StationResultSummaries\"" +
            filter.WhereClause +
            " GROUP BY " + diagnosticExpression + " ORDER BY \"Count\" DESC, \"DiagnosticCode\" ASC LIMIT 20",
            filter);
        using var reader = command.ExecuteReader();
        var results = new List<StationDiagnosticAggregate>();
        while (reader.Read())
        {
            results.Add(new StationDiagnosticAggregate(
                ReadRequiredString(reader, "DiagnosticCode"),
                ReadInt32(reader, "Count")));
        }

        return results;
    }

    private static IReadOnlyList<StationHourlyOutcomeAggregate> ExecuteHourlyAggregates(
        DbConnection connection,
        StationResultSqlFilter filter)
    {
        const string hourExpression = "strftime('%Y-%m-%dT%H:00:00+00:00', \"CompletedAtUtc\")";
        const string outcomeColumns = "\"Outcome\", \"InspectionStatus\", \"ExecutionOutcome\", \"DecisionOutcome\", \"HasJudgmentSignal\"";
        using var command = CreateResultCommand(
            connection,
            "SELECT " + hourExpression + " AS \"HourUtc\", " + outcomeColumns +
            ", COUNT(*) AS \"Count\", 0 AS \"ExecutionTimeTotal\" FROM \"StationResultSummaries\"" +
            filter.WhereClause +
            " GROUP BY " + hourExpression + ", " + outcomeColumns +
            " ORDER BY \"HourUtc\" ASC LIMIT " + (StationResultQueryBudget.MaximumHourlyTrendPoints * 16).ToString(CultureInfo.InvariantCulture),
            filter);
        using var reader = command.ExecuteReader();
        var results = new List<StationHourlyOutcomeAggregate>();
        while (reader.Read())
        {
            var hour = ReadDateTimeOffset(reader, "HourUtc");
            results.Add(new StationHourlyOutcomeAggregate(
                hour,
                new StationOutcomeAggregate(
                    string.Empty,
                    ReadRequiredString(reader, "Outcome"),
                    ReadNullableString(reader, "InspectionStatus"),
                    ReadNullableString(reader, "ExecutionOutcome"),
                    ReadNullableString(reader, "DecisionOutcome"),
                    ReadNullableBoolean(reader, "HasJudgmentSignal"),
                    ReadInt32(reader, "Count"),
                    0)));
        }

        return results;
    }

    private static StationResultStatisticsViewModel BuildStatistics(
        StationResultWindow window,
        IReadOnlyList<StationOutcomeAggregate> overall,
        IReadOnlyList<StationOutcomeAggregate> byStation,
        IReadOnlyList<StationDiagnosticAggregate> diagnostics,
        IReadOnlyList<StationHourlyOutcomeAggregate> hourly)
    {
        var overallStatistics = StationOutcomeStatisticsBuilder.Combine(overall.Select(ToOutcomeStatistics));
        var overallCount = overall.Sum(item => item.Count);
        return new StationResultStatisticsViewModel
        {
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            OutcomeStatistics = overallStatistics,
            AverageExecutionTimeMs = overallCount == 0
                ? 0
                : overall.Sum(item => item.ExecutionTimeTotal) / overallCount,
            ByStation = byStation
                .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StationOutcomeBreakdownViewModel
                {
                    StationId = group.Key,
                    OutcomeStatistics = StationOutcomeStatisticsBuilder.Combine(group.Select(ToOutcomeStatistics)),
                    AverageExecutionTimeMs = group.Sum(item => item.Count) == 0
                        ? 0
                        : group.Sum(item => item.ExecutionTimeTotal) / group.Sum(item => item.Count)
                })
                .OrderByDescending(item => item.TotalAttemptCount)
                .ThenBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList(),
            ByDiagnosticCode = diagnostics
                .Select(item => new StationDiagnosticBreakdownViewModel
                {
                    DiagnosticCode = item.DiagnosticCode,
                    Count = item.Count
                })
                .ToList(),
            HourlyTrend = hourly
                .GroupBy(item => item.HourUtc)
                .Select(group => new StationOutcomeTrendViewModel
                {
                    HourUtc = group.Key,
                    OutcomeStatistics = StationOutcomeStatisticsBuilder.Combine(group.Select(item => ToOutcomeStatistics(item.Outcome)))
                })
                .OrderBy(item => item.HourUtc)
                .Take(StationResultQueryBudget.MaximumHourlyTrendPoints)
                .ToList()
        };
    }

    private static InspectionOutcomeStatistics ToOutcomeStatistics(StationOutcomeAggregate aggregate)
    {
        var summary = new StationResultSummaryDto
        {
            Outcome = Enum.TryParse<RuntimeRunOutcome>(aggregate.Outcome, true, out var outcome)
                ? outcome
                : RuntimeRunOutcome.Error,
            InspectionStatus = Enum.TryParse<ClearVision.Product.Core.Enums.InspectionStatus>(aggregate.InspectionStatus, true, out var status)
                ? status
                : null,
            ExecutionOutcome = Enum.TryParse<ExecutionOutcome>(aggregate.ExecutionOutcome, true, out var executionOutcome)
                ? executionOutcome
                : null,
            DecisionOutcome = Enum.TryParse<DecisionOutcome>(aggregate.DecisionOutcome, true, out var decisionOutcome)
                ? decisionOutcome
                : null,
            HasJudgmentSignal = aggregate.HasJudgmentSignal
        };
        var canonical = StationCanonicalOutcomeProjection.Resolve(summary);
        var count = aggregate.Count;
        return InspectionOutcomeClassifier.Classify(canonical) switch
        {
            CanonicalInspectionOutcomeKind.Ok => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                ExecutionSucceededCount = count,
                ValidDecisionCount = count,
                OkCount = count
            },
            CanonicalInspectionOutcomeKind.Ng => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                ExecutionSucceededCount = count,
                ValidDecisionCount = count,
                NgCount = count
            },
            CanonicalInspectionOutcomeKind.Undetermined => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                ExecutionSucceededCount = count,
                UndeterminedCount = count
            },
            CanonicalInspectionOutcomeKind.NotApplicable => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                ExecutionSucceededCount = count,
                NotApplicableCount = count
            },
            CanonicalInspectionOutcomeKind.Invalid => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                ExecutionSucceededCount = count,
                InvalidCount = count
            },
            CanonicalInspectionOutcomeKind.Failed => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                FailedCount = count
            },
            CanonicalInspectionOutcomeKind.Cancelled => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                CancelledCount = count
            },
            CanonicalInspectionOutcomeKind.TimedOut => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                TimedOutCount = count
            },
            CanonicalInspectionOutcomeKind.Skipped => new InspectionOutcomeStatistics
            {
                TotalAttemptCount = count,
                SkippedCount = count
            },
            _ => new InspectionOutcomeStatistics { TotalAttemptCount = count }
        };
    }

    private static DbCommand CreateResultCommand(
        DbConnection connection,
        string commandText,
        StationResultSqlFilter filter,
        params StationResultSqlParameter[] additionalParameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var parameter in filter.Parameters.Concat(additionalParameters))
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Name;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        return command;
    }

    private static StationResultSummaryEntity ReadResultEntity(DbDataReader reader) =>
        new()
        {
            Id = ReadInt32(reader, "Id"),
            StationId = ReadRequiredString(reader, "StationId"),
            SequenceId = ReadInt64(reader, "SequenceId"),
            MessageId = ReadRequiredString(reader, "MessageId"),
            RunId = ReadRequiredString(reader, "RunId"),
            PackageId = ReadRequiredString(reader, "PackageId"),
            PackageName = ReadRequiredString(reader, "PackageName"),
            PackageVersion = ReadRequiredString(reader, "PackageVersion"),
            FlowHash = ReadRequiredString(reader, "FlowHash"),
            PackageFlowHash = ReadNullableString(reader, "PackageFlowHash"),
            ExecutionFlowHash = ReadNullableString(reader, "ExecutionFlowHash"),
            ExecutionSnapshotId = ReadNullableGuid(reader, "ExecutionSnapshotId"),
            ProjectRevision = ReadNullableInt64(reader, "ProjectRevision"),
            DecisionConfigurationHash = ReadNullableString(reader, "DecisionConfigurationHash"),
            ExecutionRunMode = ReadNullableString(reader, "ExecutionRunMode"),
            ImageId = ReadRequiredString(reader, "ImageId"),
            Outcome = ReadRequiredString(reader, "Outcome"),
            InspectionStatus = ReadNullableString(reader, "InspectionStatus"),
            ExecutionOutcome = ReadNullableString(reader, "ExecutionOutcome"),
            DecisionOutcome = ReadNullableString(reader, "DecisionOutcome"),
            HasJudgmentSignal = ReadNullableBoolean(reader, "HasJudgmentSignal"),
            DecisionSource = ReadNullableString(reader, "DecisionSource"),
            ReasonCode = ReadNullableString(reader, "ReasonCode"),
            ExecutionTimeMs = ReadInt64(reader, "ExecutionTimeMs"),
            DiagnosticCode = ReadRequiredString(reader, "DiagnosticCode"),
            DiagnosticMessage = ReadNullableString(reader, "DiagnosticMessage"),
            PrimaryOutputsPreviewJson = ReadRequiredString(reader, "PrimaryOutputsPreviewJson"),
            StartedAtUtc = ReadDateTimeOffset(reader, "StartedAtUtc"),
            CompletedAtUtc = ReadDateTimeOffset(reader, "CompletedAtUtc"),
            CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
            ReceivedAtUtc = ReadDateTimeOffset(reader, "ReceivedAtUtc")
        };

    private static string ReadRequiredString(DbDataReader reader, string column) =>
        ReadNullableString(reader, column) ?? string.Empty;

    private static string? ReadNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static int ReadInt32(DbDataReader reader, string column) =>
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static long ReadInt64(DbDataReader reader, string column) =>
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static long? ReadNullableInt64(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(DbDataReader reader, string column) =>
        Convert.ToDouble(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static bool? ReadNullableBoolean(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static Guid? ReadNullableGuid(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value is Guid guid
            ? guid
            : Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : null;
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(timestamp.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
                : timestamp),
            _ when DateTimeOffset.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Station result column '{column}' is not a valid timestamp.")
        };
    }

    private sealed record StationResultSqlFilter(
        string WhereClause,
        IReadOnlyList<StationResultSqlParameter> Parameters);

    private sealed record StationResultSqlParameter(string Name, object? Value);

    private sealed record StationOutcomeAggregate(
        string StationId,
        string Outcome,
        string? InspectionStatus,
        string? ExecutionOutcome,
        string? DecisionOutcome,
        bool? HasJudgmentSignal,
        int Count,
        double ExecutionTimeTotal);

    private sealed record StationDiagnosticAggregate(string DiagnosticCode, int Count);

    private sealed record StationHourlyOutcomeAggregate(
        DateTimeOffset HourUtc,
        StationOutcomeAggregate Outcome);

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }
}
