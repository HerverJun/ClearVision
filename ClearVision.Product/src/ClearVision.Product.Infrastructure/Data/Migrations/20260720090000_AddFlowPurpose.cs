using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260720090000_AddFlowPurpose")]
    public partial class AddFlowPurpose : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Flow_Purpose",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Flow_Purpose",
                table: "Projects");
        }
    }
}
