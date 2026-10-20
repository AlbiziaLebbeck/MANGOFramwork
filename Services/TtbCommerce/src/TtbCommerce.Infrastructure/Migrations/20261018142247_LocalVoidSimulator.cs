using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalVoidSimulator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockVoids",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockVoids", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockVoids_InstructionId",
                schema: "ttb",
                table: "MockVoids",
                column: "InstructionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockVoids_TransactionId",
                schema: "ttb",
                table: "MockVoids",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockVoids",
                schema: "ttb");
        }
    }
}
