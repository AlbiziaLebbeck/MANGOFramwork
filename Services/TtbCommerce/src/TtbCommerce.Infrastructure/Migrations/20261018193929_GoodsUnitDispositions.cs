using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GoodsUnitDispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoodsUnits",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitIndex = table.Column<int>(type: "integer", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Restocked = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsUnits", x => x.Id);
                    table.CheckConstraint("CK_GoodsUnits_Index", "\"UnitIndex\" >= 0");
                    table.ForeignKey(
                        name: "FK_GoodsUnits_Cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsUnits_Entitlements_EntitlementId",
                        column: x => x.EntitlementId,
                        principalSchema: "ttb",
                        principalTable: "Entitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsUnits_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsUnits_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsUnits_CaseId",
                schema: "ttb",
                table: "GoodsUnits",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsUnits_EntitlementId",
                schema: "ttb",
                table: "GoodsUnits",
                column: "EntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsUnits_OrderId",
                schema: "ttb",
                table: "GoodsUnits",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsUnits_OrderLineId_UnitIndex",
                schema: "ttb",
                table: "GoodsUnits",
                columns: new[] { "OrderLineId", "UnitIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ttb."GoodsUnits") THEN
                    RAISE EXCEPTION 'Refusing to discard digital return evidence. Use a compatible forward migration and retain the original records.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "GoodsUnits",
                schema: "ttb");
        }
    }
}
