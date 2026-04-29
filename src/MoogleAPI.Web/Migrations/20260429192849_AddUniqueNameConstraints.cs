using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNameConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate rows before adding unique indexes, keeping the lowest Id per (Name, GameId).
            migrationBuilder.Sql(@"
                DELETE FROM ""Characters""
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"") FROM ""Characters"" GROUP BY ""Name"", ""GameId""
                );");

            migrationBuilder.Sql(@"
                DELETE FROM ""Monsters""
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"") FROM ""Monsters"" GROUP BY ""Name"", ""GameId""
                );");

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_Name_GameId",
                table: "Monsters",
                columns: new[] { "Name", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name_GameId",
                table: "Characters",
                columns: new[] { "Name", "GameId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Monsters_Name_GameId",
                table: "Monsters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_Name_GameId",
                table: "Characters");
        }
    }
}
