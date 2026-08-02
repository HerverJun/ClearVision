using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260802000000_AddStationPackageIdentityProjection")]
public partial class AddStationPackageIdentityProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SourceProjectId",
            table: "StationPackageRecords",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "SourceProjectRevision",
            table: "StationPackageRecords",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "DecisionConfigurationHash",
            table: "StationPackageRecords",
            type: "TEXT",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SourceProjectId", table: "StationPackageRecords");
        migrationBuilder.DropColumn(name: "SourceProjectRevision", table: "StationPackageRecords");
        migrationBuilder.DropColumn(name: "DecisionConfigurationHash", table: "StationPackageRecords");
    }
}
