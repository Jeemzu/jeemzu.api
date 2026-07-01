using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JeemzuApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyAndCampaignSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GameStateJson = table.Column<string>(type: "text", nullable: false),
                    CharacterSummaryJson = table.Column<string>(type: "text", nullable: false),
                    CurrentLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastPlayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_Users_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HostUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxPlayers = table.Column<int>(type: "integer", nullable: false),
                    RpgSessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrentGamePhase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentTurnUsername = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parties_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PartyMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CharacterName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CharacterClass = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsHost = table.Column<bool>(type: "boolean", nullable: false),
                    TurnOrder = table.Column<int>(type: "integer", nullable: false),
                    ConnectionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ControlledByUsername = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyMembers_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_HostUserId",
                table: "Campaigns",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CampaignId",
                table: "Parties",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_Code",
                table: "Parties",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_PartyMembers_ConnectionId",
                table: "PartyMembers",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyMembers_PartyId",
                table: "PartyMembers",
                column: "PartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartyMembers");

            migrationBuilder.DropTable(
                name: "Parties");

            migrationBuilder.DropTable(
                name: "Campaigns");
        }
    }
}
