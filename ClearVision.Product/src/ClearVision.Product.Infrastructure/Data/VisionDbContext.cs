// VisionDbContext.cs
// VisionDbContext实现
// 作者：蘅芜君

using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Data;

/// <summary>
/// 视觉检测数据库上下文
/// </summary>
public class VisionDbContext : DbContext
{
    public VisionDbContext(DbContextOptions<VisionDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 工程表
    /// </summary>
    public DbSet<Project> Projects { get; set; } = null!;

    /// <summary>
    /// 算子表
    /// </summary>
    public DbSet<Operator> Operators { get; set; } = null!;

    /// <summary>
    /// 检测结果表
    /// </summary>
    public DbSet<InspectionResult> InspectionResults { get; set; } = null!;

    /// <summary>
    /// 缺陷表
    /// </summary>
    public DbSet<Defect> Defects { get; set; } = null!;

    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    public DbSet<StationNodeEntity> StationNodes { get; set; } = null!;

    public DbSet<StationResultSummaryEntity> StationResultSummaries { get; set; } = null!;

    public DbSet<StationHealthSnapshotEntity> StationHealthSnapshots { get; set; } = null!;

    public DbSet<StationConnectionEventEntity> StationConnectionEvents { get; set; } = null!;

    public DbSet<StationAlarmEventEntity> StationAlarmEvents { get; set; } = null!;

    public DbSet<StationCommandRecordEntity> StationCommandRecords { get; set; } = null!;

    public DbSet<StationSyncCursorEntity> StationSyncCursors { get; set; } = null!;

    public DbSet<StationLogSummaryEntity> StationLogSummaries { get; set; } = null!;

    public DbSet<StationAuditRecordEntity> StationAuditRecords { get; set; } = null!;

    public DbSet<StationPackageRecordEntity> StationPackageRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置 Project 实体
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Version).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PersistenceRevision).IsRequired().HasDefaultValue(0L);
            entity.Property(e => e.GlobalSettings).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>());
            entity.Property(e => e.GlobalVariables).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, ProjectVariableJson.Options),
                v => System.Text.Json.JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(v, ProjectVariableJson.Options) ?? new ProjectGlobalVariableSchema());
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.LastOpenedAt);



            // 配置 Table Splitting: Project 与 OperatorFlow 共享 Projects 表
            entity.HasOne(e => e.Flow)
                .WithOne()
                .HasForeignKey<OperatorFlow>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("Projects");
        });


        // 配置 OperatorFlow 实体 (Table Splitting Part 2)
        modelBuilder.Entity<OperatorFlow>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(e => e.Id);

            // 映射属性到指定列名 (保持与原来 OwnsOne 的命名习惯兼容)
            entity.Property(e => e.Name).HasColumnName("Flow_Name").IsRequired().HasMaxLength(200);
            entity.Property(e => e.DecisionConfiguration)
                .HasColumnName("Flow_DecisionConfiguration")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<DecisionConfiguration>(v, (System.Text.Json.JsonSerializerOptions?)null));

            // 解决共享列冲突：映射到不同列
            entity.Property(e => e.CreatedAt).HasColumnName("Flow_CreatedAt");
            entity.Property(e => e.ModifiedAt).HasColumnName("Flow_ModifiedAt");
            entity.Property(e => e.IsDeleted).HasColumnName("Flow_IsDeleted");

            // 配置与 Operator 的关系
            entity.HasMany(e => e.Operators)
                .WithOne()
                .HasForeignKey(o => o.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // 配置 Connections (Owned Collection)
            entity.OwnsMany(e => e.Connections, connection =>
            {
                connection.HasKey("Id");
                connection.Property(c => c.SourceOperatorId).IsRequired();
                connection.Property(c => c.SourcePortId).IsRequired();
                connection.Property(c => c.TargetOperatorId).IsRequired();
                connection.Property(c => c.TargetPortId).IsRequired();
            });
        });
        // 配置 Operator 实体
        modelBuilder.Entity<Operator>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.IsEnabled).IsRequired();
            entity.Property(e => e.ExecutionStatus).IsRequired();

            // 配置 Position 为 Owned Entity
            entity.OwnsOne(e => e.Position, position =>
            {
                position.Property(p => p.X).HasColumnName("PositionX");
                position.Property(p => p.Y).HasColumnName("PositionY");
            });

            // 配置 InputPorts 为 Owned Entity Collection
            entity.OwnsMany(e => e.InputPorts, port =>
            {
                port.HasKey("Id");
                port.Property(p => p.Name).IsRequired().HasMaxLength(100);
                port.Property(p => p.Direction).IsRequired();
                port.Property(p => p.DataType).IsRequired();
                port.Property(p => p.IsRequired).IsRequired();
            });

            // 配置 OutputPorts 为 Owned Entity Collection
            entity.OwnsMany(e => e.OutputPorts, port =>
            {
                port.HasKey("Id");
                port.Property(p => p.Name).IsRequired().HasMaxLength(100);
                port.Property(p => p.Direction).IsRequired();
                port.Property(p => p.DataType).IsRequired();
                port.Property(p => p.IsRequired).IsRequired();
            });

            // 配置 Parameters 为 Owned Entity Collection
            entity.OwnsMany(e => e.Parameters, param =>
            {
                param.HasKey("Id");
                param.Property(p => p.Name).IsRequired().HasMaxLength(100);
                param.Property(p => p.DisplayName).HasMaxLength(200);
                param.Property(p => p.Description).HasMaxLength(1000);
                param.Property(p => p.DataType).IsRequired().HasMaxLength(50);
                param.Property(p => p.DefaultValueJson).HasMaxLength(4000);
                param.Property(p => p.ValueJson).HasMaxLength(4000);
                param.Property(p => p.MinValueJson).HasMaxLength(1000);
                param.Property(p => p.MaxValueJson).HasMaxLength(1000);
                param.Property(p => p.IsRequired).IsRequired();
            });
        });

        // 配置 InspectionResult 实体
        modelBuilder.Entity<InspectionResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.ExecutionOutcome);
            entity.Property(e => e.DecisionOutcome);
            entity.Property(e => e.HasJudgmentSignal);
            entity.Property(e => e.DecisionSource).HasMaxLength(500);
            entity.Property(e => e.ReasonCode).HasMaxLength(200);
            entity.Property(e => e.ProcessingTimeMs).IsRequired();
            entity.Property(e => e.InspectionTime).IsRequired();
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.InspectionTime);
            entity.HasIndex(e => e.Status);
        });

        // 配置 Defect 实体
        modelBuilder.Entity<Defect>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.X).IsRequired();
            entity.Property(e => e.Y).IsRequired();
            entity.Property(e => e.Width).IsRequired();
            entity.Property(e => e.Height).IsRequired();
            entity.Property(e => e.ConfidenceScore).IsRequired();
            entity.HasIndex(e => e.InspectionResultId);
        });

        // 配置 User 实体
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.LastLoginAt);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.IsActive);
        });

        ConfigureStationSyncEntities(modelBuilder);
    }

    private static void ConfigureStationSyncEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StationNodeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.StationName).HasMaxLength(200);
            entity.Property(e => e.LineName).HasMaxLength(200);
            entity.Property(e => e.AreaName).HasMaxLength(200);
            entity.Property(e => e.WorkcellName).HasMaxLength(200);
            entity.Property(e => e.InspectionNodeName).HasMaxLength(200);
            entity.Property(e => e.CameraAlias).HasMaxLength(200);
            entity.Property(e => e.StationRole).HasMaxLength(100);
            entity.Property(e => e.MachineName).HasMaxLength(200);
            entity.Property(e => e.OnlineState).HasMaxLength(50);
            entity.Property(e => e.RuntimeState).HasMaxLength(50);
            entity.HasIndex(e => e.StationId).IsUnique();
            entity.HasIndex(e => e.LastSeenAtUtc);
        });

        modelBuilder.Entity<StationResultSummaryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.MessageId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.RunId).HasMaxLength(128);
            entity.Property(e => e.PackageId).HasMaxLength(128);
            entity.Property(e => e.PackageVersion).HasMaxLength(80);
            entity.Property(e => e.Outcome).HasMaxLength(50);
            entity.Property(e => e.InspectionStatus).HasMaxLength(50);
            entity.Property(e => e.ExecutionOutcome).HasMaxLength(50);
            entity.Property(e => e.DecisionOutcome).HasMaxLength(50);
            entity.Property(e => e.DecisionSource).HasMaxLength(500);
            entity.Property(e => e.ReasonCode).HasMaxLength(200);
            entity.Property(e => e.DiagnosticCode).HasMaxLength(200);
            entity.HasIndex(e => new { e.StationId, e.SequenceId }).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.CompletedAtUtc });
            entity.HasIndex(e => new { e.StationId, e.ExecutionOutcome, e.DecisionOutcome });
            entity.HasIndex(e => e.MessageId);
        });

        modelBuilder.Entity<StationHealthSnapshotEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.MessageId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.RuntimeState).HasMaxLength(50);
            entity.HasIndex(e => new { e.StationId, e.SequenceId }).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<StationConnectionEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(80);
            entity.HasIndex(e => new { e.StationId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<StationAlarmEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AlarmId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Severity).HasMaxLength(50);
            entity.Property(e => e.Code).HasMaxLength(200);
            entity.HasIndex(e => e.AlarmId).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.IsActive });
        });

        modelBuilder.Entity<StationCommandRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CommandId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.CommandType).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.CommandId).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.StationId, e.Status });
        });

        modelBuilder.Entity<StationSyncCursorEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.StationId).IsUnique();
        });

        modelBuilder.Entity<StationLogSummaryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StationId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.MessageId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Level).HasMaxLength(20);
            entity.Property(e => e.Source).HasMaxLength(200);
            entity.HasIndex(e => new { e.StationId, e.SequenceId }).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.TimestampUtc });
        });

        modelBuilder.Entity<StationAuditRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AuditId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(120);
            entity.HasIndex(e => e.AuditId).IsUnique();
            entity.HasIndex(e => new { e.TargetStationId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<StationPackageRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PackageId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.PackageName).HasMaxLength(200);
            entity.Property(e => e.PackageVersion).HasMaxLength(80);
            entity.Property(e => e.PackageKind).HasMaxLength(40).HasDefaultValue("Production");
            entity.Property(e => e.Sha256).HasMaxLength(128);
            entity.HasIndex(e => e.PackageId).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
        });
    }
}
