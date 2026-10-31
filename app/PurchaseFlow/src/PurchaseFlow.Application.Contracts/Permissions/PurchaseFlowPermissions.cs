namespace PurchaseFlow.Permissions;

public static class PurchaseFlowPermissions
{
    public const string GroupName = "PurchaseFlow";

    public const string Requests = GroupName + ".Requests";
    public const string EditRequests = GroupName + ".Requests.Edit";
    public const string ReviewRequests = GroupName + ".Requests.Review";

    public const string ViewReceipts = GroupName + ".Receipts.View";
    public const string RegisterReceipts = GroupName + ".Receipts.Register";
}

