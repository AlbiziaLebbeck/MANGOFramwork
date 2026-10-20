using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalMockBankCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockCredits",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Scenario = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NoticeJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockCredits_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "ttb",
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockCredits_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockCredits_AttemptId",
                schema: "ttb",
                table: "MockCredits",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockCredits_OrderId_RequestKey",
                schema: "ttb",
                table: "MockCredits",
                columns: new[] { "OrderId", "RequestKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockCredits",
                schema: "ttb");
        }
    }
}
