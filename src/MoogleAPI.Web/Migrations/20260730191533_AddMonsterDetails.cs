using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMonsterDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Monsters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Absorbs",
                table: "Monsters",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gil",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MagicPoints",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Popularity",
                table: "Monsters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Weaknesses",
                table: "Monsters",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WikiBacklinks",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WikiPageLength",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_Category",
                table: "Monsters",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_Popularity",
                table: "Monsters",
                column: "Popularity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Monsters_Category",
                table: "Monsters");

            migrationBuilder.DropIndex(
                name: "IX_Monsters_Popularity",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Absorbs",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Gil",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "MagicPoints",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Weaknesses",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "WikiBacklinks",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "WikiPageLength",
                table: "Monsters");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Monsters",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
