namespace PurchaseFlow.Permissions;

public static class PurchaseFlowPermissions
{
    public const string GroupName = "PurchaseFlow";

    public const string Requests = GroupName + ".Requests";
    public const string EditRequests = GroupName + ".Requests.Edit";
    public const string ReviewRequests = GroupName + ".Requests.Review";

    public const string ViewReceipts = GroupName + ".Receipts.View";
    public const string RegisterReceipts = GroupName + ".Receipts.Register";

    // 第二轮第 1 批：主数据（供应商/物料档案）。查看与维护拆开，
    // 演示里所有业务角色都能看档案用于选择，只有管理员能维护。
    public const string MasterData = GroupName + ".MasterData";
    public const string ManageMasterData = GroupName + ".MasterData.Manage";
}
