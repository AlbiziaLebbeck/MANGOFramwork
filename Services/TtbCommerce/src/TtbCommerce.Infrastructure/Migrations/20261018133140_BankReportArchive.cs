using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankReportArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reports",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    FileDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    EncryptedSha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EncryptedBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PlaintextSha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReplayOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParserProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TotalsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.CheckConstraint("CK_Reports_SizeRevision", "\"EncryptedBytes\" > 0 AND \"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_Reports_Reports_ReplayOfId",
                        column: x => x.ReplayOfId,
                        principalSchema: "ttb",
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportFetches",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReportArchiveId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFetches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportFetches_Reports_ReportArchiveId",
                        column: x => x.ReportArchiveId,
                        principalSchema: "ttb",
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportRows",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportArchiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    SemanticHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportRows_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportRows_Reports_ReportArchiveId",
                        column: x => x.ReportArchiveId,
                        principalSchema: "ttb",
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportFetches_Environment_MerchantId_FileName",
                schema: "ttb",
                table: "ReportFetches",
                columns: new[] { "Environment", "MerchantId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportFetches_ReportArchiveId",
                schema: "ttb",
                table: "ReportFetches",
                column: "ReportArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_BankTransactionId",
                schema: "ttb",
                table: "ReportRows",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_Outcome_Id",
                schema: "ttb",
                table: "ReportRows",
                columns: new[] { "Outcome", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_ReportArchiveId_Sequence",
                schema: "ttb",
                table: "ReportRows",
                columns: new[] { "ReportArchiveId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Environment_MerchantId_FileName_EncryptedSha256",
                schema: "ttb",
                table: "Reports",
                columns: new[] { "Environment", "MerchantId", "FileName", "EncryptedSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Environment_MerchantId_FileName_Revision",
                schema: "ttb",
                table: "Reports",
                columns: new[] { "Environment", "MerchantId", "FileName", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReplayOfId",
                schema: "ttb",
                table: "Reports",
                column: "ReplayOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_State_CreatedAt",
                schema: "ttb",
                table: "Reports",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportFetches",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "ReportRows",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Reports",
                schema: "ttb");
        }
    }
}
