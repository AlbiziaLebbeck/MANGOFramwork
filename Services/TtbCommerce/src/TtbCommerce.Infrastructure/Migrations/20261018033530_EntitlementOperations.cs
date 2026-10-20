using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EntitlementOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntitlementOperations",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntitlementOperations_Entitlements_EntitlementId",
                        column: x => x.EntitlementId,
                        principalSchema: "ttb",
                        principalTable: "Entitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementOperations_EntitlementId",
                schema: "ttb",
                table: "EntitlementOperations",
                column: "EntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementOperations_OperationKey",
                schema: "ttb",
                table: "EntitlementOperations",
                column: "OperationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntitlementOperations",
                schema: "ttb");
        }
    }
}
