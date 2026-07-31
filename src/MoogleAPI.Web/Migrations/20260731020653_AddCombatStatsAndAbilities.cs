using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatStatsAndAbilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Abilities",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attack",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Defense",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Drops",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Evasion",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MagicAttack",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MagicDefense",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Speed",
                table: "Monsters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Steals",
                table: "Monsters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Abilities",
                table: "Characters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Abilities",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Attack",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Defense",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Drops",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Evasion",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "MagicAttack",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "MagicDefense",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Speed",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Steals",
                table: "Monsters");

            migrationBuilder.DropColumn(
                name: "Abilities",
                table: "Characters");
        }
    }
}
