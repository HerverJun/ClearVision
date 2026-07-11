using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260714000000_AddStationExecutionIdentity")]
public partial class AddStationExecutionIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddIdentityColumns(migrationBuilder, "StationNodes", includeCurrentRunId: true);
        AddIdentityColumns(migrationBuilder, "StationResultSummaries", includeCurrentRunId: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropIdentityColumns(migrationBuilder, "StationNodes", includeCurrentRunId: true);
        DropIdentityColumns(migrationBuilder, "StationResultSummaries", includeCurrentRunId: false);
    }

    private static void AddIdentityColumns(MigrationBuilder migrationBuilder, string table, bool includeCurrentRunId)
    {
        migrationBuilder.AddColumn<string>(name: "PackageFlowHash", table: table, type: "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ExecutionFlowHash", table: table, type: "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ExecutionSnapshotId", table: table, type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<long>(name: "ProjectRevision", table: table, type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DecisionConfigurationHash", table: table, type: "TEXT", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ExecutionRunMode", table: table, type: "TEXT", maxLength: 50, nullable: true);
        if (includeCurrentRunId)
        {
            migrationBuilder.AddColumn<string>(name: "FlowHash", table: table, type: "TEXT", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "CurrentRunId", table: table, type: "TEXT", maxLength: 128, nullable: true);
        }
    }

    private static void DropIdentityColumns(MigrationBuilder migrationBuilder, string table, bool includeCurrentRunId)
    {
        migrationBuilder.DropColumn(name: "PackageFlowHash", table: table);
        migrationBuilder.DropColumn(name: "ExecutionFlowHash", table: table);
        migrationBuilder.DropColumn(name: "ExecutionSnapshotId", table: table);
        migrationBuilder.DropColumn(name: "ProjectRevision", table: table);
        migrationBuilder.DropColumn(name: "DecisionConfigurationHash", table: table);
        migrationBuilder.DropColumn(name: "ExecutionRunMode", table: table);
        if (includeCurrentRunId)
        {
            migrationBuilder.DropColumn(name: "FlowHash", table: table);
            migrationBuilder.DropColumn(name: "CurrentRunId", table: table);
        }
    }
}
