using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Acme.Product.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260601000000_AddStationPackageKind")]
    public partial class AddStationPackageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageKind",
                table: "StationPackageRecords",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "Production");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackageKind",
                table: "StationPackageRecords");
        }
    }
}
