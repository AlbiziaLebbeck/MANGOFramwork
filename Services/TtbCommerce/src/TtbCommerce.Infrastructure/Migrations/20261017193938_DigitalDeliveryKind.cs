using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DigitalDeliveryKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DigitalKind",
                schema: "ttb",
                table: "Variants",
                type: "text",
                nullable: false,
                defaultValue: "InventoryItem");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigitalKind",
                schema: "ttb",
                table: "Variants");
        }
    }
}
