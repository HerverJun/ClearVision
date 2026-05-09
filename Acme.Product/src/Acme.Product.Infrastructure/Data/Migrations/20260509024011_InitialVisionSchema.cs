using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Product.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialVisionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessingTimeMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    OutputImage = table.Column<byte[]>(type: "BLOB", nullable: true),
                    InspectionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OutputDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    AnalysisDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    FlowVersionHash = table.Column<string>(type: "TEXT", nullable: true),
                    CalibrationBundleId = table.Column<string>(type: "TEXT", nullable: true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Flow_Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Flow_CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Flow_ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Flow_IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    GlobalSettings = table.Column<string>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationAlarmEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlarmId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationAlarmEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationAuditRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuditId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TargetStationId = table.Column<string>(type: "TEXT", nullable: true),
                    CommandId = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadSummary = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationAuditRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationCommandRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CommandType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IssuedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    ResultMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorDetail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationCommandRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationConnectionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationConnectionEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationHealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RuntimeState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProcessUptimeSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    CpuUsagePercent = table.Column<double>(type: "REAL", nullable: true),
                    WorkingSetMb = table.Column<long>(type: "INTEGER", nullable: false),
                    PrivateMemoryMb = table.Column<long>(type: "INTEGER", nullable: false),
                    DiskFreeMb = table.Column<long>(type: "INTEGER", nullable: false),
                    DiskTotalMb = table.Column<long>(type: "INTEGER", nullable: false),
                    SpoolPendingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SpoolBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CameraStatusSummary = table.Column<string>(type: "TEXT", nullable: true),
                    PlcStatusSummary = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentPackageId = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentPackageHealth = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationHealthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationLogSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventId = table.Column<string>(type: "TEXT", nullable: true),
                    MessageTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    RenderedMessage = table.Column<string>(type: "TEXT", nullable: false),
                    ExceptionType = table.Column<string>(type: "TEXT", nullable: true),
                    ExceptionMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: true),
                    RunId = table.Column<string>(type: "TEXT", nullable: true),
                    PackageId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationLogSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StationName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AreaName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WorkcellName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InspectionNodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CameraAlias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StationRole = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: true),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IpAddressHint = table.Column<string>(type: "TEXT", nullable: true),
                    MacAddressHash = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OnlineState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RuntimeState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CurrentPackageId = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentPackageName = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentPackageVersion = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Remark = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationPackageRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FlowHash = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationPackageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationResultSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FlowHash = table.Column<string>(type: "TEXT", nullable: false),
                    ImageId = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    InspectionStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ExecutionTimeMs = table.Column<long>(type: "INTEGER", nullable: false),
                    DiagnosticCode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DiagnosticMessage = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryOutputsPreviewJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationResultSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationSyncCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastPersistedSequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastReceivedHealthSequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastReceivedLogSequenceId = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationSyncCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Defects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InspectionResultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    AnnotationData = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Defects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Defects_InspectionResults_InspectionResultId",
                        column: x => x.InspectionResultId,
                        principalTable: "InspectionResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperatorConnection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceOperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePortId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetOperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetPortId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorFlowId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorConnection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorConnection_Projects_OperatorFlowId",
                        column: x => x.OperatorFlowId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Operators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionX = table.Column<double>(type: "REAL", nullable: false),
                    PositionY = table.Column<double>(type: "REAL", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExecutionStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ExecutionTimeMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Operators_InputPorts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators_InputPorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_InputPorts_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Operators_OutputPorts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators_OutputPorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_OutputPorts_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parameter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DefaultValueJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ValueJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MinValueJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MaxValueJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parameter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parameter_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_InspectionResultId",
                table: "Defects",
                column: "InspectionResultId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectionTime",
                table: "InspectionResults",
                column: "InspectionTime");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_ProjectId",
                table: "InspectionResults",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_Status",
                table: "InspectionResults",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorConnection_OperatorFlowId",
                table: "OperatorConnection",
                column: "OperatorFlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_ProjectId",
                table: "Operators",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_InputPorts_OperatorId",
                table: "Operators_InputPorts",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_OutputPorts_OperatorId",
                table: "Operators_OutputPorts",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Parameter_OperatorId",
                table: "Parameter",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LastOpenedAt",
                table: "Projects",
                column: "LastOpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_StationAlarmEvents_AlarmId",
                table: "StationAlarmEvents",
                column: "AlarmId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationAlarmEvents_StationId_IsActive",
                table: "StationAlarmEvents",
                columns: new[] { "StationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StationAuditRecords_AuditId",
                table: "StationAuditRecords",
                column: "AuditId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationAuditRecords_TargetStationId_CreatedAtUtc",
                table: "StationAuditRecords",
                columns: new[] { "TargetStationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationCommandRecords_CommandId",
                table: "StationCommandRecords",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationCommandRecords_StationId_CreatedAtUtc",
                table: "StationCommandRecords",
                columns: new[] { "StationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationCommandRecords_StationId_Status",
                table: "StationCommandRecords",
                columns: new[] { "StationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StationConnectionEvents_StationId_CreatedAtUtc",
                table: "StationConnectionEvents",
                columns: new[] { "StationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationHealthSnapshots_StationId_CreatedAtUtc",
                table: "StationHealthSnapshots",
                columns: new[] { "StationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationHealthSnapshots_StationId_SequenceId",
                table: "StationHealthSnapshots",
                columns: new[] { "StationId", "SequenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationLogSummaries_StationId_SequenceId",
                table: "StationLogSummaries",
                columns: new[] { "StationId", "SequenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationLogSummaries_StationId_TimestampUtc",
                table: "StationLogSummaries",
                columns: new[] { "StationId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationNodes_LastSeenAtUtc",
                table: "StationNodes",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StationNodes_StationId",
                table: "StationNodes",
                column: "StationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationPackageRecords_CreatedAtUtc",
                table: "StationPackageRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StationPackageRecords_PackageId",
                table: "StationPackageRecords",
                column: "PackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationResultSummaries_MessageId",
                table: "StationResultSummaries",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_StationResultSummaries_StationId_CompletedAtUtc",
                table: "StationResultSummaries",
                columns: new[] { "StationId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StationResultSummaries_StationId_SequenceId",
                table: "StationResultSummaries",
                columns: new[] { "StationId", "SequenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationSyncCursors_StationId",
                table: "StationSyncCursors",
                column: "StationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive",
                table: "Users",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Defects");

            migrationBuilder.DropTable(
                name: "OperatorConnection");

            migrationBuilder.DropTable(
                name: "Operators_InputPorts");

            migrationBuilder.DropTable(
                name: "Operators_OutputPorts");

            migrationBuilder.DropTable(
                name: "Parameter");

            migrationBuilder.DropTable(
                name: "StationAlarmEvents");

            migrationBuilder.DropTable(
                name: "StationAuditRecords");

            migrationBuilder.DropTable(
                name: "StationCommandRecords");

            migrationBuilder.DropTable(
                name: "StationConnectionEvents");

            migrationBuilder.DropTable(
                name: "StationHealthSnapshots");

            migrationBuilder.DropTable(
                name: "StationLogSummaries");

            migrationBuilder.DropTable(
                name: "StationNodes");

            migrationBuilder.DropTable(
                name: "StationPackageRecords");

            migrationBuilder.DropTable(
                name: "StationResultSummaries");

            migrationBuilder.DropTable(
                name: "StationSyncCursors");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "InspectionResults");

            migrationBuilder.DropTable(
                name: "Operators");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
