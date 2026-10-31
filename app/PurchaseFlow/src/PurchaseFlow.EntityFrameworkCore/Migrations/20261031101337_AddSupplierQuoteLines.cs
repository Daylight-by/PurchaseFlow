using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierQuoteLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSupplierQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierQuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseRequestLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Specification = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSupplierQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSupplierQuoteLines_AppSupplierQuotes_SupplierQuoteId",
                        column: x => x.SupplierQuoteId,
                        principalTable: "AppSupplierQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSupplierQuoteLines_SupplierQuoteId",
                table: "AppSupplierQuoteLines",
                column: "SupplierQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSupplierQuoteLines_TenantId_SupplierQuoteId",
                table: "AppSupplierQuoteLines",
                columns: new[] { "TenantId", "SupplierQuoteId" });

            // P1-1：OrderNumber 上限 64 → 128。
            // 为什么手工补这条：EF Core 10 的迁移差异器对 SQLite 只比较存储类型
            // （TEXT 不变），不比较 MaxLength 这类 facet，dotnet ef 自动生成时会漏掉
            // 这一列变更（已用探针迁移验证：空迁移）。这里按 EF 标准 AlterColumn 形态
            // 手工补齐，保证迁移历史与模型（64→128）同步。
            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "AppPurchaseOrders",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            // P0-1 数据回填（一）：迁移前的历史报价只有总额、没有逐项明细。
            // 给这类报价补一条“汇总行”（数量 1、单价 = 原报价 Amount），使历史报价
            // 也满足“报价总额 = 明细合计”的不变式；报价 Amount 本身不改写。
            migrationBuilder.Sql(
                """
                INSERT INTO "AppSupplierQuoteLines"
                    ("Id", "TenantId", "SupplierQuoteId", "PurchaseRequestLineId", "ItemName", "Specification", "Quantity", "UnitPrice")
                SELECT
                    lower(hex(randomblob(4)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(6))),
                    q."TenantId", q."Id", NULL, '历史报价汇总（迁移）', '', '1', CAST(q."Amount" AS TEXT)
                FROM "AppSupplierQuotes" q
                WHERE NOT EXISTS (SELECT 1 FROM "AppSupplierQuoteLines" l WHERE l."SupplierQuoteId" = q."Id");
                """);

            // P0-1 数据回填（二）：历史采购订单的 Amount 可能不等于其明细合计
            // （旧实现里订单金额直接取报价总额）。给差额 >= 0.005 的历史订单补一条
            // “差额调整明细”（数量 1、单价 = 差额），使订单总额 = 明细合计成立；
            // 刻意不修改历史订单的 Amount，避免改写既有业务数据。
            migrationBuilder.Sql(
                """
                INSERT INTO "AppPurchaseOrderLines"
                    ("Id", "TenantId", "PurchaseOrderId", "ItemName", "Specification", "Quantity", "UnitPrice", "ReceivedQuantity")
                SELECT
                    lower(hex(randomblob(4)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(2)))||'-'||lower(hex(randomblob(6))),
                    o."TenantId", o."Id", '历史金额调整（迁移）', '', '1',
                    printf('%.2f', ROUND(o."Amount" - IFNULL((SELECT SUM(ROUND(l."Quantity" * l."UnitPrice", 2)) FROM "AppPurchaseOrderLines" l WHERE l."PurchaseOrderId" = o."Id"), 0), 2)),
                    0
                FROM "AppPurchaseOrders" o
                WHERE ABS(o."Amount" - IFNULL((SELECT SUM(ROUND(l."Quantity" * l."UnitPrice", 2)) FROM "AppPurchaseOrderLines" l WHERE l."PurchaseOrderId" = o."Id"), 0)) >= 0.005;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 先清掉 Up() 里补的两类迁移标记明细，再删表/回退列，保证回退可逆。
            // Down 仅用于开发环境回退：若已存在超过 64 字符的订单号，回退后模型层
            // 重新限制为 64 会带来校验风险（SQLite 物理层 TEXT 不截断），属已知限制。
            migrationBuilder.Sql(
                """DELETE FROM "AppPurchaseOrderLines" WHERE "ItemName" = '历史金额调整（迁移）';""");

            migrationBuilder.Sql(
                """DELETE FROM "AppSupplierQuoteLines" WHERE "ItemName" = '历史报价汇总（迁移）';""");

            migrationBuilder.DropTable(
                name: "AppSupplierQuoteLines");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "AppPurchaseOrders",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128);
        }
    }
}
