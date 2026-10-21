using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IsolatedBankTestConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankTestCases",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    Ref1 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref2 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CrossBorder = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRequestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTestCases", x => x.Id);
                    table.CheckConstraint("CK_BankTestCases_Amount", "\"AmountMinor\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "BankTestOperations",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    TransportError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SendFencedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTestOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTestOperations_BankTestCases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "BankTestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTestReceipts",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AckCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTestReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTestReceipts_BankTestCases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "BankTestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTestEvents",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Ref1 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref2 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    RequestUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    BankCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTestEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTestEvents_BankTestCases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "ttb",
                        principalTable: "BankTestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTestEvents_BankTestOperations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "ttb",
                        principalTable: "BankTestOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTestCases_QrId",
                schema: "ttb",
                table: "BankTestCases",
                column: "QrId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTestEvents_CaseId_Id",
                schema: "ttb",
                table: "BankTestEvents",
                columns: new[] { "CaseId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTestEvents_Kind_Id",
                schema: "ttb",
                table: "BankTestEvents",
                columns: new[] { "Kind", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTestEvents_OperationId",
                schema: "ttb",
                table: "BankTestEvents",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTestOperations_CaseId_Kind",
                schema: "ttb",
                table: "BankTestOperations",
                columns: new[] { "CaseId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTestOperations_OperationKey",
                schema: "ttb",
                table: "BankTestOperations",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTestReceipts_CaseId",
                schema: "ttb",
                table: "BankTestReceipts",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTestReceipts_MerchantId_InstructionId",
                schema: "ttb",
                table: "BankTestReceipts",
                columns: new[] { "MerchantId", "InstructionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTestEvents",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "BankTestReceipts",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "BankTestOperations",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "BankTestCases",
                schema: "ttb");
        }
    }
}
