using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

/// <summary>
/// Adds nullable canonical Station outcome fields. Null retains the original legacy
/// payload shape so historic Station rows are projected only when read.
/// </summary>
[Migration("20260712000000_AddCanonicalStationOutcomes")]
public partial class AddCanonicalStationOutcomes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DecisionOutcome",
            table: "StationResultSummaries",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DecisionSource",
            table: "StationResultSummaries",
            type: "TEXT",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExecutionOutcome",
            table: "StationResultSummaries",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "HasJudgmentSignal",
            table: "StationResultSummaries",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReasonCode",
            table: "StationResultSummaries",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StationResultSummaries_StationId_ExecutionOutcome_DecisionOutcome",
            table: "StationResultSummaries",
            columns: new[] { "StationId", "ExecutionOutcome", "DecisionOutcome" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StationResultSummaries_StationId_ExecutionOutcome_DecisionOutcome",
            table: "StationResultSummaries");

        migrationBuilder.DropColumn(name: "DecisionOutcome", table: "StationResultSummaries");
        migrationBuilder.DropColumn(name: "DecisionSource", table: "StationResultSummaries");
        migrationBuilder.DropColumn(name: "ExecutionOutcome", table: "StationResultSummaries");
        migrationBuilder.DropColumn(name: "HasJudgmentSignal", table: "StationResultSummaries");
        migrationBuilder.DropColumn(name: "ReasonCode", table: "StationResultSummaries");
    }
}
