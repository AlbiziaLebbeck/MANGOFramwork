using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DigitalEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigitalAssets",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Ready = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalAssets", x => x.Id);
                    table.UniqueConstraint("AK_DigitalAssets_Key", x => x.Key);
                    table.CheckConstraint("CK_DigitalAssets_Size", "\"ByteLength\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Entitlements",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitIndex = table.Column<int>(type: "integer", nullable: false),
                    DigitalKind = table.Column<string>(type: "text", nullable: false),
                    DigitalAssetKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    VariantName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DownloadRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DownloadRequests = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.Id);
                    table.CheckConstraint("CK_Entitlements_Unit", "\"UnitIndex\" BETWEEN 0 AND 98 AND \"DownloadRequests\" >= 0");
                    table.ForeignKey(
                        name: "FK_Entitlements_DigitalAssets_DigitalAssetKey",
                        column: x => x.DigitalAssetKey,
                        principalSchema: "ttb",
                        principalTable: "DigitalAssets",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entitlements_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entitlements_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entitlements_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DownloadEvents",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadEvents_Entitlements_EntitlementId",
                        column: x => x.EntitlementId,
                        principalSchema: "ttb",
                        principalTable: "Entitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadEvents_EntitlementId_CreatedAt",
                schema: "ttb",
                table: "DownloadEvents",
                columns: new[] { "EntitlementId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_DigitalAssetKey",
                schema: "ttb",
                table: "Entitlements",
                column: "DigitalAssetKey");

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_OrderId",
                schema: "ttb",
                table: "Entitlements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_OrderLineId_UnitIndex",
                schema: "ttb",
                table: "Entitlements",
                columns: new[] { "OrderLineId", "UnitIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_ShopperId_Id",
                schema: "ttb",
                table: "Entitlements",
                columns: new[] { "ShopperId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadEvents",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Entitlements",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "DigitalAssets",
                schema: "ttb");
        }
    }
}
