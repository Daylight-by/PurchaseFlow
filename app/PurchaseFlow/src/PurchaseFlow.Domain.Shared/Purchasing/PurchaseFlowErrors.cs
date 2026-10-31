namespace PurchaseFlow.Purchasing;

public static class PurchaseFlowErrors
{
    public const string NotEditable = "PurchaseFlow:NotEditable";
    public const string InvalidStatus = "PurchaseFlow:InvalidStatus";
    public const string NeedTwoQuotes = "PurchaseFlow:NeedTwoQuotes";
    public const string NeedSelectedQuote = "PurchaseFlow:NeedSelectedQuote";
    public const string NeedSelectionReason = "PurchaseFlow:NeedSelectionReason";
    public const string InvalidAmount = "PurchaseFlow:InvalidAmount";
    public const string NeedReviewComment = "PurchaseFlow:NeedReviewComment";
    public const string DuplicateSupplier = "PurchaseFlow:DuplicateSupplier";
    public const string DuplicateOrderReference = "PurchaseFlow:DuplicateOrderReference";
    public const string NeedPurchaseLines = "PurchaseFlow:NeedPurchaseLines";
    public const string InvalidItemName = "PurchaseFlow:InvalidItemName";
    public const string InvalidQuantity = "PurchaseFlow:InvalidQuantity";
    public const string InvalidUnitPrice = "PurchaseFlow:InvalidUnitPrice";

    // P0-1/P1-3 新增：报价必须逐项覆盖需求明细；更新/替换明细时 Id 必须可解释。
    public const string QuoteLineMismatch = "PurchaseFlow:QuoteLineMismatch";
    public const string DuplicateLineId = "PurchaseFlow:DuplicateLineId";
    public const string LineNotInRequest = "PurchaseFlow:LineNotInRequest";

    // 第二轮第 1 批（主数据）：供应商/物料档案的编码与启停用规则。
    public const string InvalidSupplierCode = "PurchaseFlow:InvalidSupplierCode";
    public const string DuplicateSupplierCode = "PurchaseFlow:DuplicateSupplierCode";
    public const string SupplierInactive = "PurchaseFlow:SupplierInactive";
    public const string InvalidMaterialCode = "PurchaseFlow:InvalidMaterialCode";
    public const string DuplicateMaterialCode = "PurchaseFlow:DuplicateMaterialCode";
    public const string MaterialInactive = "PurchaseFlow:MaterialInactive";

    // 第二轮第 2 批（分级审批）：二级审批必须换人，避免同一个人绕过两级控制。
    public const string SecondApprovalRequiresDifferentUser = "PurchaseFlow:SecondApprovalRequiresDifferentUser";

    // 第二轮第 3 批（报价有效期）：过期报价不能选择/提交，避免用失效价格成交。
    public const string QuoteExpired = "PurchaseFlow:QuoteExpired";

    // 第三轮（三单匹配 + 付款）：发票逐行匹配订单与收货，不匹配即拒绝。
    public const string InvoiceLinesRequired = "PurchaseFlow:InvoiceLinesRequired";
    public const string InvoiceNumberDuplicate = "PurchaseFlow:InvoiceNumberDuplicate";
    public const string InvalidInvoiceDate = "PurchaseFlow:InvalidInvoiceDate";
    public const string InvoiceLineNotInOrder = "PurchaseFlow:InvoiceLineNotInOrder";
    public const string InvoicePriceMismatch = "PurchaseFlow:InvoicePriceMismatch";
    public const string InvoiceQuantityExceedsReceived = "PurchaseFlow:InvoiceQuantityExceedsReceived";
    public const string InvoiceNeedsReceivedGoods = "PurchaseFlow:InvoiceNeedsReceivedGoods";
    public const string PaymentNotAllowedForInvoice = "PurchaseFlow:PaymentNotAllowedForInvoice";
    public const string PaymentAmountExceedsInvoice = "PurchaseFlow:PaymentAmountExceedsInvoice";
    public const string InvalidPaymentAmount = "PurchaseFlow:InvalidPaymentAmount";
    public const string DuplicatePaymentForInvoice = "PurchaseFlow:DuplicatePaymentForInvoice";
    public const string PaymentNotPending = "PurchaseFlow:PaymentNotPending";
    public const string PaymentNotApproved = "PurchaseFlow:PaymentNotApproved";
    public const string InvalidReceiptDate = "PurchaseFlow:InvalidReceiptDate";
    public const string InvalidReceiptKey = "PurchaseFlow:InvalidReceiptKey";
    public const string InvalidReceiptLines = "PurchaseFlow:InvalidReceiptLines";
    public const string DuplicateReceiptLine = "PurchaseFlow:DuplicateReceiptLine";
    public const string ReceiptLineNotInOrder = "PurchaseFlow:ReceiptLineNotInOrder";
    public const string ReceiptExceedsRemaining = "PurchaseFlow:ReceiptExceedsRemaining";
    public const string ReceiptOrderNotReceivable = "PurchaseFlow:ReceiptOrderNotReceivable";
    public const string ReceiptIdempotencyConflict = "PurchaseFlow:ReceiptIdempotencyConflict";
    public const string ReceiptConcurrencyConflict = "PurchaseFlow:ReceiptConcurrencyConflict";
    public const string ReceiptQuantityPrecision = "PurchaseFlow:ReceiptQuantityPrecision";
    public const string ReceiptNotFound = "PurchaseFlow:ReceiptNotFound";
}
