using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoogleAPI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGameMainSeriesFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMainSeries",
                table: "Games",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every game in the table at this point is a numbered entry or a direct sequel to
            // one, so the whole catalogue backfills to true and the spin-offs arrive already
            // flagged. The column default stays false for exactly that reason: from here on, a
            // new row is far more likely to be a spin-off than another numbered entry.
            migrationBuilder.Sql("""UPDATE "Games" SET "IsMainSeries" = TRUE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMainSeries",
                table: "Games");
        }
    }
}
