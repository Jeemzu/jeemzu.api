using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JeemzuApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueScorePerUserPerGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_UserId",
                table: "Scores");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_UserId_GameId",
                table: "Scores",
                columns: new[] { "UserId", "GameId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_UserId_GameId",
                table: "Scores");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_UserId",
                table: "Scores",
                column: "UserId");
        }
    }
}
