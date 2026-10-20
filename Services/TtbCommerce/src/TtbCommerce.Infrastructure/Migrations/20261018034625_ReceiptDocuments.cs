using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Number = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FileSha256 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FileBytes = table.Column<long>(type: "bigint", nullable: true),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.CheckConstraint("CK_Documents_Ready", "\"State\" <> 'Ready' OR (\"FileKey\" IS NOT NULL AND \"FileSha256\" IS NOT NULL AND \"FileBytes\" > 0 AND \"ReadyAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Documents_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BankTransactionId",
                schema: "ttb",
                table: "Documents",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Number",
                schema: "ttb",
                table: "Documents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrderId_Kind",
                schema: "ttb",
                table: "Documents",
                columns: new[] { "OrderId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents",
                schema: "ttb");
        }
    }
}
