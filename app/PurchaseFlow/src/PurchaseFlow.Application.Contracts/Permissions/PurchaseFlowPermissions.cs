namespace PurchaseFlow.Permissions;

public static class PurchaseFlowPermissions
{
    public const string GroupName = "PurchaseFlow";

    public const string Requests = GroupName + ".Requests";
    public const string EditRequests = GroupName + ".Requests.Edit";
    public const string ReviewRequests = GroupName + ".Requests.Review";

    /// <summary>
    /// 第二轮第 2 批：高金额采购的二级审批权限。仅授予管理员角色，
    /// 审核员只有一级审批权限，避免“一个人批完整个流程”。
    /// </summary>
    public const string ApproveHighValue = GroupName + ".Requests.ApproveHighValue";

    public const string ViewReceipts = GroupName + ".Receipts.View";
    public const string RegisterReceipts = GroupName + ".Receipts.Register";

    // 第二轮第 1 批：主数据（供应商/物料档案）。查看与维护拆开，
    // 演示里所有业务角色都能看档案用于选择，只有管理员能维护。
    public const string MasterData = GroupName + ".MasterData";
    public const string ManageMasterData = GroupName + ".MasterData.Manage";

    // 第三轮（三单匹配 + 付款）：发票查看/登记、付款审批。
    // 发票登记给采购员与管理员；付款审批只给管理员，形成“业务登记、管理审批”的职责分离。
    public const string ViewInvoices = GroupName + ".Invoices.View";
    public const string RegisterInvoices = GroupName + ".Invoices.Register";
    public const string ApprovePayments = GroupName + ".Invoices.ApprovePayments";
}
