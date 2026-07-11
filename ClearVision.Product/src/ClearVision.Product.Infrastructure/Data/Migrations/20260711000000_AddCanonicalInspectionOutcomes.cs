using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260711000000_AddCanonicalInspectionOutcomes")]
    public partial class AddCanonicalInspectionOutcomes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExecutionOutcome",
                table: "InspectionResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecisionOutcome",
                table: "InspectionResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionSource",
                table: "InspectionResults",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "InspectionResults",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExecutionOutcome", table: "InspectionResults");
            migrationBuilder.DropColumn(name: "DecisionOutcome", table: "InspectionResults");
            migrationBuilder.DropColumn(name: "DecisionSource", table: "InspectionResults");
            migrationBuilder.DropColumn(name: "ReasonCode", table: "InspectionResults");
        }
    }
}
