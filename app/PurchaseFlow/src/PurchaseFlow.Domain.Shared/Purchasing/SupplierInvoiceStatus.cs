namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：供应商发票状态。
/// 当前实现里“不匹配的发票直接拒绝登记”，因此保存下来的发票一律处于 Matched；
/// 付款申请提交后进入 PaymentRequested，登记付款后进入 Paid。
/// </summary>
public enum SupplierInvoiceStatus
{
    Matched = 0,
    PaymentRequested = 1,
    Paid = 2
}
