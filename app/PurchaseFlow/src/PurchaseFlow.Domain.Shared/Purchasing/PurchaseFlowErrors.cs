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
