using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPopularityAndCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Popularity",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WikiBacklinks",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WikiPageLength",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Top = table.Column<int>(type: "integer", nullable: false),
                    Left = table.Column<int>(type: "integer", nullable: false),
                    Right = table.Column<int>(type: "integer", nullable: false),
                    Bottom = table.Column<int>(type: "integer", nullable: false),
                    Element = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    CardClass = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Popularity",
                table: "Characters",
                column: "Popularity");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameId",
                table: "Cards",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Name_GameId",
                table: "Cards",
                columns: new[] { "Name", "GameId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Characters_Popularity",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "WikiBacklinks",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "WikiPageLength",
                table: "Characters");
        }
    }
}
