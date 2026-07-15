using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookDistributionAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryNameSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LibraryName",
                table: "ReceiptVouchers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LibraryName",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LibraryName",
                table: "ReceiptVouchers");

            migrationBuilder.DropColumn(
                name: "LibraryName",
                table: "Invoices");
        }
    }
}
