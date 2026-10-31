using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptVersion",
                table: "AppPurchaseOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptVersion",
                table: "AppPurchaseOrders");
        }
    }
}
