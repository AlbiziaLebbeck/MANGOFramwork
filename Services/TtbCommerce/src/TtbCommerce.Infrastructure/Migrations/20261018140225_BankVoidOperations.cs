using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankVoidOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Voids",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeliveryReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SendFencedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmationSource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EnquiryCount = table.Column<int>(type: "integer", nullable: false),
                    LastEnquiryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnquiryLeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    EnquiryLeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Voids", x => x.Id);
                    table.CheckConstraint("CK_Voids_AmountCount", "\"AmountMinor\" > 0 AND \"EnquiryCount\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_Voids_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Voids_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoidRequests",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoidOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    RawResponse = table.Column<byte[]>(type: "bytea", nullable: false),
                    ResponseSha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoidRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoidRequests_Voids_VoidOperationId",
                        column: x => x.VoidOperationId,
                        principalSchema: "ttb",
                        principalTable: "Voids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoidRequests_RequestUid",
                schema: "ttb",
                table: "VoidRequests",
                column: "RequestUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoidRequests_VoidOperationId",
                schema: "ttb",
                table: "VoidRequests",
                column: "VoidOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_Voids_BankTransactionId",
                schema: "ttb",
                table: "Voids",
                column: "BankTransactionId",
                unique: true,
                filter: "\"State\" <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_Voids_Environment_MerchantId_TransactionId",
                schema: "ttb",
                table: "Voids",
                columns: new[] { "Environment", "MerchantId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Voids_OperationKey",
                schema: "ttb",
                table: "Voids",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Voids_OrderId",
                schema: "ttb",
                table: "Voids",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoidRequests",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Voids",
                schema: "ttb");
        }
    }
}
