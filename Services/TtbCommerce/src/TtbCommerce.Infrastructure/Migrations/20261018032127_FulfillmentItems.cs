using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FulfillmentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fulfillments",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    DeliveryMethod = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Carrier = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OperatorNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fulfillments", x => x.Id);
                    table.CheckConstraint("CK_Fulfillments_Quantity", "\"Quantity\" > 0 AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Fulfillments_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fulfillments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fulfillments_Kind_State_Id",
                schema: "ttb",
                table: "Fulfillments",
                columns: new[] { "Kind", "State", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Fulfillments_OrderId",
                schema: "ttb",
                table: "Fulfillments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Fulfillments_OrderLineId",
                schema: "ttb",
                table: "Fulfillments",
                column: "OrderLineId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fulfillments",
                schema: "ttb");
        }
    }
}
