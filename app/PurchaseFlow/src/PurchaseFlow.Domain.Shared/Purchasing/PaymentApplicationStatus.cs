namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮：付款申请状态。PedingApproval → Approved → Paid 单向流转；
/// 只有匹配发票才能申请，只有已批准申请才能登记付款。
/// </summary>
public enum PaymentApplicationStatus
{
    PendingApproval = 0,
    Approved = 1,
    Paid = 2
}
