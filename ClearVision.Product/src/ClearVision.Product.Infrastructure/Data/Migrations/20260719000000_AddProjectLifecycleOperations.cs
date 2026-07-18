using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260719000000_AddProjectLifecycleOperations")]
public partial class AddProjectLifecycleOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectLifecycleOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ClientOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                PayloadFingerprintVersion = table.Column<int>(type: "INTEGER", nullable: false),
                PayloadFingerprint = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                ProjectDescription = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                ExpectedPersistenceRevision = table.Column<long>(type: "INTEGER", nullable: true),
                ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CleanupStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CleanupAuthorityOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                CleanupAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                LastCleanupErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CleanupNextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectLifecycleOperations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectLifecycleOperations_CleanupStatus_CleanupNextAttemptAtUtc",
            table: "ProjectLifecycleOperations",
            columns: new[] { "CleanupStatus", "CleanupNextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectLifecycleOperations_ExpiresAtUtc",
            table: "ProjectLifecycleOperations",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectLifecycleOperations_ProjectId",
            table: "ProjectLifecycleOperations",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectLifecycleOperations_Status_UpdatedAtUtc",
            table: "ProjectLifecycleOperations",
            columns: new[] { "Status", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectLifecycleOperations_UserId_Kind_ClientOperationId",
            table: "ProjectLifecycleOperations",
            columns: new[] { "UserId", "Kind", "ClientOperationId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Project lifecycle operation authority is forward-only and cannot be removed by migration rollback.");
    }
}
