using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualRefundEvidenceAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceEvidence",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceEvidence_Cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReturns",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    EffectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReturns", x => x.Id);
                    table.CheckConstraint("CK_GoodsReturns_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_GoodsReturns_Cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReturns_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReturns_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManualRefunds",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    AffectsOrderPayment = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExternalPaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidenceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualRefunds", x => x.Id);
                    table.CheckConstraint("CK_ManualRefunds_Amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_ManualRefunds_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualRefunds_Cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualRefunds_Cases_SourceCaseId",
                        column: x => x.SourceCaseId,
                        principalSchema: "ttb",
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEvidence_CaseId_Id",
                schema: "ttb",
                table: "FinanceEvidence",
                columns: new[] { "CaseId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEvidence_CaseId_Sha256",
                schema: "ttb",
                table: "FinanceEvidence",
                columns: new[] { "CaseId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEvidence_Key",
                schema: "ttb",
                table: "FinanceEvidence",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReturns_CaseId",
                schema: "ttb",
                table: "GoodsReturns",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReturns_EffectKey",
                schema: "ttb",
                table: "GoodsReturns",
                column: "EffectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReturns_OrderId",
                schema: "ttb",
                table: "GoodsReturns",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReturns_OrderLineId",
                schema: "ttb",
                table: "GoodsReturns",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualRefunds_BankTransactionId_State",
                schema: "ttb",
                table: "ManualRefunds",
                columns: new[] { "BankTransactionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualRefunds_CaseId",
                schema: "ttb",
                table: "ManualRefunds",
                column: "CaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualRefunds_OrderId",
                schema: "ttb",
                table: "ManualRefunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualRefunds_SourceCaseId",
                schema: "ttb",
                table: "ManualRefunds",
                column: "SourceCaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ttb."ManualRefunds") OR EXISTS (SELECT 1 FROM ttb."FinanceEvidence") OR EXISTS (SELECT 1 FROM ttb."GoodsReturns") THEN
                    RAISE EXCEPTION 'Refusing to discard financial/return evidence. Use a compatible forward migration and retain the original records.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "FinanceEvidence",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "GoodsReturns",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "ManualRefunds",
                schema: "ttb");
        }
    }
}
