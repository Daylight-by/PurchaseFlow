using System;
using System.Threading.Tasks;
using PurchaseFlow.Permissions;
using Volo.Abp.PermissionManagement;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 演示角色的权限授予规则（第二轮修复）。
///
/// 为什么集中到一个类：
/// 1. ABP 的权限校验**不会隐含父子关系**——`ApproveHighValue` 有权限不代表有 `Requests`。
///    而采购应用服务和详情页在类/页面级都要求 `Requests`，所以管理员如果只被授予
///    `ApproveHighValue`，登录后会打不开采购页、也调用不了二级审批，高金额请求会卡死。
/// 2. DbMigrator 部署时调用它；真实授权测试（不使用 AlwaysAllow）也调用它，
///    避免这类“种子少授一个父权限”的问题只能靠人工在部署后发现。
/// </summary>
public static class DemoPermissionGrants
{
    public const string AdminRole = "admin";
    public const string BuyerRole = "buyer";
    public const string ReviewerRole = "reviewer";
    public const string ReceiverRole = "receiver";

    public static async Task GrantAsync(IPermissionManager permissionManager)
    {
        ArgumentNullException.ThrowIfNull(permissionManager);

        // 管理员是“全流程演示账号”：显式授予全部采购业务权限。
        // 尤其 Requests 必须显式授予，否则 [Authorize(Requests)] 会挡住管理员。
        foreach (var permission in new[]
                 {
                     PurchaseFlowPermissions.Requests,
                     PurchaseFlowPermissions.EditRequests,
                     PurchaseFlowPermissions.ReviewRequests,
                     PurchaseFlowPermissions.ApproveHighValue,
                     PurchaseFlowPermissions.ViewReceipts,
                     PurchaseFlowPermissions.RegisterReceipts,
                     PurchaseFlowPermissions.MasterData,
                     PurchaseFlowPermissions.ManageMasterData,
                     // 第三轮：发票查看/登记、付款审批（管理员是唯一能批付款的演示角色）。
                     PurchaseFlowPermissions.ViewInvoices,
                     PurchaseFlowPermissions.RegisterInvoices,
                     PurchaseFlowPermissions.ApprovePayments
                 })
        {
            await permissionManager.SetForRoleAsync(AdminRole, permission, true);
        }

        // 业务角色：查看采购需求 + 各自动作权限；都能查看主数据档案，但不能维护；
        // 只有 receiver 能登记收货，保持既有演示账号分工。
        await GrantBusinessRoleAsync(permissionManager, BuyerRole, PurchaseFlowPermissions.EditRequests);
        await GrantBusinessRoleAsync(permissionManager, ReviewerRole, PurchaseFlowPermissions.ReviewRequests);
        await GrantBusinessRoleAsync(permissionManager, ReceiverRole, PurchaseFlowPermissions.RegisterReceipts);

        // 第三轮：采购员可以登记发票并申请付款；审核员/收货员只能查看。
        // 付款审批权限只属于管理员（见上），形成“业务登记、管理审批”的职责分离。
        await permissionManager.SetForRoleAsync(BuyerRole, PurchaseFlowPermissions.RegisterInvoices, true);
    }

    private static async Task GrantBusinessRoleAsync(
        IPermissionManager permissionManager,
        string roleName,
        string actionPermission)
    {
        await permissionManager.SetForRoleAsync(roleName, PurchaseFlowPermissions.Requests, true);
        await permissionManager.SetForRoleAsync(roleName, actionPermission, true);
        await permissionManager.SetForRoleAsync(roleName, PurchaseFlowPermissions.ViewReceipts, true);
        await permissionManager.SetForRoleAsync(roleName, PurchaseFlowPermissions.RegisterReceipts,
            string.Equals(actionPermission, PurchaseFlowPermissions.RegisterReceipts, StringComparison.Ordinal));
        // 业务角色都能“查看”供应商/物料档案（用于报价与需求明细选择），
        // 但不授予 ManageMasterData —— 维护权限只给管理员。
        await permissionManager.SetForRoleAsync(roleName, PurchaseFlowPermissions.MasterData, true);
        // 第三轮：所有业务角色都能查看发票/付款进度，登记与审批权限按角色单独授予。
        await permissionManager.SetForRoleAsync(roleName, PurchaseFlowPermissions.ViewInvoices, true);
    }
}
