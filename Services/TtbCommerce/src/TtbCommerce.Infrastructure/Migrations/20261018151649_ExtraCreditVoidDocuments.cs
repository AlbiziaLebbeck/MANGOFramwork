using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtraCreditVoidDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_BankTransactionId",
                schema: "ttb",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_OrderId_Kind",
                schema: "ttb",
                table: "Documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                schema: "ttb",
                table: "Voids",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "AffectsOrderPayment",
                schema: "ttb",
                table: "Voids",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                schema: "ttb",
                table: "Documents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BankTransactionId_Kind",
                schema: "ttb",
                table: "Documents",
                columns: new[] { "BankTransactionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrderId_Kind",
                schema: "ttb",
                table: "Documents",
                columns: new[] { "OrderId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Receipt'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_BankTransactionId_Kind",
                schema: "ttb",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_OrderId_Kind",
                schema: "ttb",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AffectsOrderPayment",
                schema: "ttb",
                table: "Voids");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                schema: "ttb",
                table: "Voids",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                schema: "ttb",
                table: "Documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BankTransactionId",
                schema: "ttb",
                table: "Documents",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrderId_Kind",
                schema: "ttb",
                table: "Documents",
                columns: new[] { "OrderId", "Kind" },
                unique: true);
        }
    }
}
