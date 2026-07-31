using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratedImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeneratedImageUrl",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratedImageUrl",
                table: "Characters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeneratedImageUrl",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "GeneratedImageUrl",
                table: "Characters");
        }
    }
}
