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
        requests.AddChild(PurchaseFlowPermissions.ReviewRequests, L("Permission:ReviewRequests"));
        myGroup.AddPermission(PurchaseFlowPermissions.ViewReceipts, L("Permission:ViewReceipts"));
        myGroup.AddPermission(PurchaseFlowPermissions.RegisterReceipts, L("Permission:RegisterReceipts"));

        // 第二轮第 1 批：主数据查看/维护分离，避免“能看档案”被误当成“能改档案”。
        var masterData = myGroup.AddPermission(PurchaseFlowPermissions.MasterData, L("Permission:MasterData"));
        masterData.AddChild(PurchaseFlowPermissions.ManageMasterData, L("Permission:ManageMasterData"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PurchaseFlowResource>(name);
    }
}

