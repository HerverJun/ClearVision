using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations;

[DbContext(typeof(VisionDbContext))]
[Migration("20260808000000_AddProjectLifecycleCommandPayload")]
public partial class AddProjectLifecycleCommandPayload : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CommandPayloadJson",
            table: "ProjectLifecycleOperations",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Project lifecycle operation authority is forward-only and cannot be removed by migration rollback.");
    }
}
