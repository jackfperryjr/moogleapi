using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddImageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageKind",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageKind",
                table: "Characters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageKind",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "ImageKind",
                table: "Characters");
        }
    }
}
