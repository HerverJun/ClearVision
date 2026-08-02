using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260802010000_AddStationCommandIdempotencyAndActivePackageIdentity")]
public partial class AddStationCommandIdempotencyAndActivePackageIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientRequestId",
            table: "StationCommandRecords",
            type: "TEXT",
            maxLength: 128,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "RequestPayloadSha256",
            table: "StationCommandRecords",
            type: "TEXT",
            maxLength: 64,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "MinStationVersion",
            table: "StationPackageRecords",
            type: "TEXT",
            maxLength: 80,
            nullable: false,
            defaultValue: "0.1.0");
        migrationBuilder.AddColumn<string>(
            name: "CurrentPackageSha256",
            table: "StationNodes",
            type: "TEXT",
            maxLength: 80,
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "SourceProjectId",
            table: "StationNodes",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "SourceProjectRevision",
            table: "StationNodes",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_StationCommandRecords_StationId_CommandType_ClientRequestId",
            table: "StationCommandRecords",
            columns: new[] { "StationId", "CommandType", "ClientRequestId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StationCommandRecords_StationId_CommandType_ClientRequestId",
            table: "StationCommandRecords");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "StationCommandRecords");
        migrationBuilder.DropColumn(name: "RequestPayloadSha256", table: "StationCommandRecords");
        migrationBuilder.DropColumn(name: "MinStationVersion", table: "StationPackageRecords");
        migrationBuilder.DropColumn(name: "CurrentPackageSha256", table: "StationNodes");
        migrationBuilder.DropColumn(name: "SourceProjectId", table: "StationNodes");
        migrationBuilder.DropColumn(name: "SourceProjectRevision", table: "StationNodes");
    }
}
