using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 第二轮第 2 批：新增一级审批人列（可空 Guid，无外键，运营/审计字段）。
            // 为什么需要落库：高金额请求的二级审批必须换人，规则校验与页面展示
            // 都依赖“一级是谁批的”；旧库历史行保持 NULL，可直接升级。
            migrationBuilder.AddColumn<Guid>(
                name: "FirstApproverUserId",
                table: "AppPurchaseRequests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退只删列：状态枚举 PendingSecondApproval=4 保留（历史数据合法性不受影响），
            // 仅丢失一级审批人信息。
            migrationBuilder.DropColumn(
                name: "FirstApproverUserId",
                table: "AppPurchaseRequests");
        }
    }
}
