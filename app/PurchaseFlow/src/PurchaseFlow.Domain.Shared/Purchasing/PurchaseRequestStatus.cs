namespace PurchaseFlow.Purchasing;

public enum PurchaseRequestStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Returned = 3,

    /// <summary>
    /// 第二轮第 2 批（分级审批）：采购金额超过阈值时，一级审批通过后进入二级待审。
    /// 数值必须追加在末尾（=4），不能插入中间，否则会改写历史数据库里已有的状态值。
    /// </summary>
    PendingSecondApproval = 4
}
