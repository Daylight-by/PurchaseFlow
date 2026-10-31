using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// One purchase line. The subtotal is calculated by the domain and returned
/// for display; it is never accepted from the client.
/// </summary>
public class PurchaseRequestLineDto
{
    /// <summary>
    /// P1-3：输出 DTO 的 Id 必须非空。能返回给页面就说明这一行已经落库，
    /// 更新时页面要把它带回来；可空 Id 是新建/更新口径混淆的根源。
    /// </summary>
    public Guid Id { get; set; }

    [Required, StringLength(100)]
    public string ItemName { get; set; } = string.Empty;

    [StringLength(100)]
    public string Specification { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.001", "9999999999999999.999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }

    public decimal Subtotal { get; set; }
}

/// <summary>
/// P1-3：创建采购需求时的明细输入，刻意不含 Id —— 新建行一律由服务端生成
/// 主键，从类型上杜绝“新建请求里携带陌生 Id”的混用。
/// </summary>
public class CreatePurchaseRequestLineDto
{
    [Required, StringLength(100)]
    public string ItemName { get; set; } = string.Empty;

    [StringLength(100)]
    public string Specification { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.001", "9999999999999999.999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// P1-3：更新采购需求时的明细输入，只有更新才允许携带 Id。
/// Id 为空表示新增行；Id 非空时应用服务会严格校验它必须属于当前需求。
/// </summary>
public class UpdatePurchaseRequestLineDto
{
    public Guid? Id { get; set; }

    [Required, StringLength(100)]
    public string ItemName { get; set; } = string.Empty;

    [StringLength(100)]
    public string Specification { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.001", "9999999999999999.999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }
}

public class CreatePurchaseRequestDto
{
    [Required, StringLength(64)]
    public string OrderReference { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Expected revenue of the related sales order (gross profit basis).</summary>
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal SaleAmount { get; set; }

    /// <summary>Purchase lines; the request amount is the sum of their subtotals.</summary>
    [Required, MinLength(1)]
    public List<CreatePurchaseRequestLineDto> Lines { get; set; } = new();
}

/// <summary>
/// P1-3：更新输入与创建输入分开，避免更新复用带可空 Id 的 DTO 时出现
/// “该新增的行带回 Id、该更新的行丢 Id”的歧义。
/// </summary>
public class UpdatePurchaseRequestDto
{
    [Required, StringLength(64)]
    public string OrderReference { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal SaleAmount { get; set; }

    [Required, MinLength(1)]
    public List<UpdatePurchaseRequestLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 供应商报价输入。P0-1：报价改为逐项录入 —— 每条需求明细对应一个报价单价，
/// 报价总额由服务端按明细汇总，不再接受手填总额，从入口就杜绝
/// “报价总额一套数、报价明细另一套数”。
/// </summary>
public class AddSupplierQuoteDto
{
    [Required, StringLength(100)]
    public string SupplierName { get; set; } = string.Empty;

    [Range(0, 3650)]
    public int DeliveryDays { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>逐项报价：必须覆盖当前采购需求的全部明细。</summary>
    [Required, MinLength(1)]
    public List<SupplierQuoteLineInputDto> Lines { get; set; } = new();
}

/// <summary>
/// P0-1：逐项报价 —— 引用需求明细 Id，并给出该供应商对这一行的报价单价。
/// 物料、规格、数量由服务端从需求明细快照，客户端无法篡改。
/// </summary>
public class SupplierQuoteLineInputDto
{
    [Required]
    public Guid RequestLineId { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }
}

public class SelectSupplierQuoteDto
{
    [Required]
    public Guid QuoteId { get; set; }

    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ReviewPurchaseRequestDto
{
    [StringLength(500)]
    public string? Comment { get; set; }
}

public class PurchaseRequestSummaryDto
{
    public Guid Id { get; set; }
    public string OrderReference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal SaleAmount { get; set; }

    /// <summary>Purchase amount calculated from the lines.</summary>
    public decimal Amount { get; set; }

    public int LineCount { get; set; }
    public PurchaseRequestStatus Status { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid? SelectedQuoteId { get; set; }
    public string? SelectionReason { get; set; }
    public DateTime CreationTime { get; set; }
}

public class PurchaseRequestListInput
{
    public string? Keyword { get; set; }
    public PurchaseRequestStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequestListDto
{
    public List<PurchaseRequestSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int AllCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingReviewCount { get; set; }
    public int ReturnedCount { get; set; }
    public int ApprovedCount { get; set; }
}

public class PurchaseDashboardDto
{
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingReviewCount { get; set; }
    public int ReturnedCount { get; set; }
    public int ApprovedCount { get; set; }
    public int TotalQuoteCount { get; set; }
    public decimal ApprovedPurchaseAmount { get; set; }
    public decimal ApprovedExpectedGrossProfit { get; set; }
    public List<PurchaseRequestSummaryDto> PendingReviews { get; set; } = new();
    public List<PurchaseRequestSummaryDto> RecentRequests { get; set; } = new();
}

public class SupplierQuoteDto
{
    public Guid Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int DeliveryDays { get; set; }
    public string? Note { get; set; }
    public decimal ExpectedGrossProfit { get; set; }
    public decimal CostPremiumToLowest { get; set; }

    /// <summary>
    /// P0-1：报价逐项明细。报价总额必须等于这些行的小计合计，
    /// 审批生成采购订单时也直接使用这里的报价（成交）单价。
    /// </summary>
    public List<SupplierQuoteLineDto> Lines { get; set; } = new();
}

/// <summary>
/// P0-1：一条报价明细（物料 / 规格 / 数量来自需求行快照，单价为该供应商的报价）。
/// </summary>
public class SupplierQuoteLineDto
{
    public Guid Id { get; set; }
    public Guid? PurchaseRequestLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class QuoteDecisionAnalysisDto
{
    public int QuoteCount { get; set; }
    public decimal? CheapestAmount { get; set; }
    public int? FastestDays { get; set; }
    public decimal? SelectedMarginPercent { get; set; }
    public bool IsReadyToSubmit { get; set; }
    public List<Guid> CheapestQuoteIds { get; set; } = new();
    public List<Guid> FastestQuoteIds { get; set; } = new();
    public List<Guid> DominatedQuoteIds { get; set; } = new();
    public List<DecisionCheckDto> Checks { get; set; } = new();
    public List<DecisionAlertDto> Warnings { get; set; } = new();
}

public class DecisionCheckDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
}

public class DecisionAlertDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ReviewEventDto
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public class PurchaseOrderLineDto
{
    public Guid Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PurchaseRequestId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int DeliveryDays { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public DateTime CreationTime { get; set; }
    public List<PurchaseOrderLineDto> Lines { get; set; } = new();
}

public class PurchaseRequestDetailDto : PurchaseRequestSummaryDto
{
    public decimal? SelectedQuoteAmount { get; set; }
    public decimal? ExpectedGrossProfit { get; set; }
    public decimal? SelectedCostPremiumToLowest { get; set; }
    public int? SelectedDeliveryDaysComparedWithLowest { get; set; }
    public List<PurchaseRequestLineDto> Lines { get; set; } = new();
    public List<SupplierQuoteDto> Quotes { get; set; } = new();
    public QuoteDecisionAnalysisDto Decision { get; set; } = new();
    public List<ReviewEventDto> History { get; set; } = new();

    /// <summary>The purchase order that was created when the request was approved.</summary>
    public PurchaseOrderDto? PurchaseOrder { get; set; }
}

public class GoodsReceiptLineInputDto
{
    [Required]
    public Guid PurchaseOrderLineId { get; set; }

    [Range(typeof(decimal), "0.001", "9999999999999999.999")]
    public decimal Quantity { get; set; }
}

public class CreateGoodsReceiptDto
{
    [Required]
    public Guid PurchaseOrderId { get; set; }

    [Required, StringLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime ReceiptDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [Required, MinLength(1)]
    public List<GoodsReceiptLineInputDto> Lines { get; set; } = new();
}

public class GoodsReceiptLineDto
{
    public Guid PurchaseOrderLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public class GoodsReceiptDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public DateTime ReceiptDate { get; set; }
    public string? Note { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<GoodsReceiptLineDto> Lines { get; set; } = new();
}

public class PurchaseOrderReceiptLineDto
{
    public Guid PurchaseOrderLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
}

public class PurchaseOrderReceiptDto
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrderStatus OrderStatus { get; set; }
    public bool IsPartiallyReceived { get; set; }
    public bool IsFullyReceived { get; set; }
    public List<PurchaseOrderReceiptLineDto> Lines { get; set; } = new();
    public List<GoodsReceiptDto> Receipts { get; set; } = new();
}
