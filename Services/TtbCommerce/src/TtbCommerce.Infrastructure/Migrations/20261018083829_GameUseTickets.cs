using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GameUseTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameTickets",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChallengeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameTickets_Entitlements_EntitlementId",
                        column: x => x.EntitlementId,
                        principalSchema: "ttb",
                        principalTable: "Entitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameTickets_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "ttb",
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameTickets_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameTickets_EntitlementId",
                schema: "ttb",
                table: "GameTickets",
                column: "EntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_GameTickets_ExpiresAt",
                schema: "ttb",
                table: "GameTickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_GameTickets_SessionId",
                schema: "ttb",
                table: "GameTickets",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GameTickets_ShopperId_CreatedAt",
                schema: "ttb",
                table: "GameTickets",
                columns: new[] { "ShopperId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameTickets_TokenHash",
                schema: "ttb",
                table: "GameTickets",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameTickets",
                schema: "ttb");
        }
    }
}
