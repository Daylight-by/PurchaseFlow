using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class HostOrderReferenceUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppPurchaseRequests_HostOrderReference",
                table: "AppPurchaseRequests",
                column: "OrderReference",
                unique: true,
                filter: "\"TenantId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppPurchaseRequests_HostOrderReference",
                table: "AppPurchaseRequests");
        }
    }
}

