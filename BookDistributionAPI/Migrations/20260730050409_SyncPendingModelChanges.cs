using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookDistributionAPI.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClearanceOutstandingAmount",
                table: "Invoices",
                type: "decimal(10,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClearancePaidAmount",
                table: "Invoices",
                type: "decimal(10,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClearanceOutstandingAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClearancePaidAmount",
                table: "Invoices");
        }
    }
}
