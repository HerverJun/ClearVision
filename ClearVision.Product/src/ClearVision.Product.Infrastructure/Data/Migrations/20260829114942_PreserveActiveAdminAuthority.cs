using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveActiveAdminAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstallationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationStates", x => x.Id);
                    table.CheckConstraint("CK_InstallationStates_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "InstallationStates"
                    ("Id", "IsCompleted", "CompletedAtUtc", "Revision")
                SELECT 1,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                       ) THEN 1 ELSE 0 END,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                       ) THEN CURRENT_TIMESTAMP ELSE NULL END,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM "Users" WHERE "IsDeleted" = 0
                       ) THEN 1 ELSE 0 END;

                CREATE TRIGGER "TR_InstallationStates_PreventReopen"
                BEFORE UPDATE OF "IsCompleted" ON "InstallationStates"
                FOR EACH ROW
                WHEN OLD."IsCompleted" = 1 AND NEW."IsCompleted" = 0
                BEGIN
                    SELECT RAISE(ABORT, 'installation completion latch cannot be reopened');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_InstallationStates_PreventReopen";""");
            migrationBuilder.DropTable(name: "InstallationStates");
        }
    }
}
