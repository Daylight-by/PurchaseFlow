using PurchaseFlow.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace PurchaseFlow.Permissions;

public class PurchaseFlowPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(PurchaseFlowPermissions.GroupName);
        var requests = myGroup.AddPermission(PurchaseFlowPermissions.Requests, L("Permission:Requests"));
        requests.AddChild(PurchaseFlowPermissions.EditRequests, L("Permission:EditRequests"));
        var review = requests.AddChild(PurchaseFlowPermissions.ReviewRequests, L("Permission:ReviewRequests"));
        // 第二轮第 2 批：二级审批权限挂在“审批”下面，只有管理员角色被授予。
        review.AddChild(PurchaseFlowPermissions.ApproveHighValue, L("Permission:ApproveHighValue"));
        myGroup.AddPermission(PurchaseFlowPermissions.ViewReceipts, L("Permission:ViewReceipts"));
        myGroup.AddPermission(PurchaseFlowPermissions.RegisterReceipts, L("Permission:RegisterReceipts"));

        // 第二轮第 1 批：主数据查看/维护分离，避免“能看档案”被误当成“能改档案”。
        var masterData = myGroup.AddPermission(PurchaseFlowPermissions.MasterData, L("Permission:MasterData"));
        masterData.AddChild(PurchaseFlowPermissions.ManageMasterData, L("Permission:ManageMasterData"));

        // 第三轮：发票查看/登记与付款审批分离，付款审批只给管理员。
        var invoices = myGroup.AddPermission(PurchaseFlowPermissions.ViewInvoices, L("Permission:ViewInvoices"));
        invoices.AddChild(PurchaseFlowPermissions.RegisterInvoices, L("Permission:RegisterInvoices"));
        invoices.AddChild(PurchaseFlowPermissions.ApprovePayments, L("Permission:ApprovePayments"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PurchaseFlowResource>(name);
    }
}

