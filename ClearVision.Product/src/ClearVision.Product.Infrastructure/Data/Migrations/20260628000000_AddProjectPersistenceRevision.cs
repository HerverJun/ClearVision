using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260628000000_AddProjectPersistenceRevision")]
    public partial class AddProjectPersistenceRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PersistenceRevision",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersistenceRevision",
                table: "Projects");
        }
    }
}
