using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "AppPurchaseOrderLines",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AppGoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppGoodsReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppGoodsReceipts_AppPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "AppPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppGoodsReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GoodsReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppGoodsReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppGoodsReceiptLines_AppGoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "AppGoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppGoodsReceiptLines_AppPurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "AppPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceiptLines_GoodsReceiptId_PurchaseOrderLineId",
                table: "AppGoodsReceiptLines",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceiptLines_PurchaseOrderLineId",
                table: "AppGoodsReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceiptLines_TenantId_PurchaseOrderLineId",
                table: "AppGoodsReceiptLines",
                columns: new[] { "TenantId", "PurchaseOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceipts_HostOrderCreatorIdempotency",
                table: "AppGoodsReceipts",
                columns: new[] { "PurchaseOrderId", "CreatorId", "IdempotencyKey" },
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceipts_TenantId_PurchaseOrderId_CreationTime",
                table: "AppGoodsReceipts",
                columns: new[] { "TenantId", "PurchaseOrderId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodsReceipts_TenantId_PurchaseOrderId_CreatorId_IdempotencyKey",
                table: "AppGoodsReceipts",
                columns: new[] { "TenantId", "PurchaseOrderId", "CreatorId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppGoodsReceiptLines");

            migrationBuilder.DropTable(
                name: "AppGoodsReceipts");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "AppPurchaseOrderLines");
        }
    }
}
