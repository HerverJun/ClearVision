using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearVision.Product.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VisionDbContext))]
    [Migration("20260619000000_AddProjectGlobalVariables")]
    public partial class AddProjectGlobalVariables : Migration
    {
        private const string EmptySchemaJson = "{\"schemaVersion\":\"1.0\",\"variables\":[],\"sourceBindings\":[],\"targetBindings\":[]}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalVariables",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: EmptySchemaJson);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalVariables",
                table: "Projects");
        }
    }
}
