using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260711010000_AddDecisionConfiguration")]
    public partial class AddDecisionConfiguration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Flow_DecisionConfiguration",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasJudgmentSignal",
                table: "InspectionResults",
                type: "INTEGER",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Flow_DecisionConfiguration",
                table: "Projects");
            migrationBuilder.DropColumn(
                name: "HasJudgmentSignal",
                table: "InspectionResults");
        }
    }
}
