using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JeemzuApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGameScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"KnowledgeChunks\" WHERE \"SourceKey\" = 'site_games';");

            migrationBuilder.DropTable(
                name: "Scores");

            migrationBuilder.DropColumn(
                name: "OptedIn",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OptedIn",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Scores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GameId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScoreValue = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_GameId_ScoreValue",
                table: "Scores",
                columns: new[] { "GameId", "ScoreValue" });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_UserId_GameId",
                table: "Scores",
                columns: new[] { "UserId", "GameId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }
    }
}
