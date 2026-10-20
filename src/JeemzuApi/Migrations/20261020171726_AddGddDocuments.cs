using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JeemzuApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GddDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PdfData = table.Column<byte[]>(type: "bytea", nullable: false),
                    DraftData = table.Column<byte[]>(type: "bytea", nullable: false),
                    PdfBytes = table.Column<long>(type: "bigint", nullable: false),
                    DraftBytes = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GddDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GddDocuments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GddDocuments_UserId_UpdatedAt",
                table: "GddDocuments",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GddDocuments");
        }
    }
}
