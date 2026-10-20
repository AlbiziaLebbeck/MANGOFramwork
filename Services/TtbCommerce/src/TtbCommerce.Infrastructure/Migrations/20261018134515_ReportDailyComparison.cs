using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportDailyComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliedProfileJson",
                schema: "ttb",
                table: "Reports",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ComparisonJson",
                schema: "ttb",
                table: "Reports",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_Environment_MerchantId_PaymentOccurredAt",
                schema: "ttb",
                table: "BankTransactions",
                columns: new[] { "Environment", "MerchantId", "PaymentOccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_Environment_MerchantId_PaymentOccurredAt",
                schema: "ttb",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "AppliedProfileJson",
                schema: "ttb",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ComparisonJson",
                schema: "ttb",
                table: "Reports");
        }
    }
}
