using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayableCharacterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlayable",
                table: "Characters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Job",
                table: "Characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Weapon",
                table: "Characters",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_GameId_IsPlayable",
                table: "Characters",
                columns: new[] { "GameId", "IsPlayable" },
                filter: "\"IsPlayable\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Characters_GameId_IsPlayable",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "IsPlayable",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Job",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Weapon",
                table: "Characters");
        }
    }
}
