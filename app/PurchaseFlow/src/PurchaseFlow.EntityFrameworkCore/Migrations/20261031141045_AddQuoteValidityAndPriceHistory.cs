using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteValidityAndPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 第二轮第 3 批：报价增加可选有效期（业务日期，含当天；NULL=长期有效）。
            // 过期判定由领域按 UTC 日期完成，这里只落库；历史报价保持 NULL 即长期有效。
            migrationBuilder.AddColumn<DateTime>(
                name: "ValidUntil",
                table: "AppSupplierQuotes",
                type: "TEXT",
                nullable: true);

            // 第二轮第 3 批：订单明细记录来源物料档案 Id（可空、无外键），
            // 用于按物料追溯历史成交价；旧订单行为 NULL 不影响既有数据。
            migrationBuilder.AddColumn<Guid>(
                name: "MaterialId",
                table: "AppPurchaseOrderLines",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退只删两列：有效期与历史价参考是附加信息，不影响既有金额/审批数据。
            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "AppSupplierQuotes");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "AppPurchaseOrderLines");
        }
    }
}
