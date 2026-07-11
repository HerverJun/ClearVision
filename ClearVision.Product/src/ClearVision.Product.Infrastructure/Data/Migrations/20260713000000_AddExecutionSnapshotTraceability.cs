using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260713000000_AddExecutionSnapshotTraceability")]
public partial class AddExecutionSnapshotTraceability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ExecutionSnapshotId", table: "InspectionResults", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<long>(name: "ProjectPersistenceRevision", table: "InspectionResults", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DecisionConfigurationHash", table: "InspectionResults", type: "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RuntimePackageId", table: "InspectionResults", type: "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ExecutionSource", table: "InspectionResults", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ExecutionRunMode", table: "InspectionResults", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ShadowRole", table: "InspectionResults", type: "TEXT", maxLength: 32, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ExecutionSnapshotId", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "ProjectPersistenceRevision", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "DecisionConfigurationHash", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "RuntimePackageId", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "ExecutionSource", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "ExecutionRunMode", table: "InspectionResults");
        migrationBuilder.DropColumn(name: "ShadowRole", table: "InspectionResults");
    }
}
