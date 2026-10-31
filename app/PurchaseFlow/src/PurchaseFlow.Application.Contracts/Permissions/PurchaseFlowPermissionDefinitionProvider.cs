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
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PurchaseFlowResource>(name);
    }
}

