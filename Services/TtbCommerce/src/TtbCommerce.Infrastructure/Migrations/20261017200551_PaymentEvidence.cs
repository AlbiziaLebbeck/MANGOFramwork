using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                schema: "ttb",
                table: "Observations",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<byte[]>(
                name: "RawPayload",
                schema: "ttb",
                table: "Observations",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "BusinessHash",
                schema: "ttb",
                table: "CallbackReceipts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SwitchFullName",
                schema: "ttb",
                table: "BankTransactions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataJson",
                schema: "ttb",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "RawPayload",
                schema: "ttb",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "BusinessHash",
                schema: "ttb",
                table: "CallbackReceipts");

            migrationBuilder.DropColumn(
                name: "SwitchFullName",
                schema: "ttb",
                table: "BankTransactions");
        }
    }
}
