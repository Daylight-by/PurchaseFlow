using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PurchaseFlow.Purchasing;

public class CreatePurchaseRequestDto
{
    [Required, StringLength(64)]
    public string OrderReference { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal SaleAmount { get; set; }
}

public class UpdatePurchaseRequestDto : CreatePurchaseRequestDto { }

public class AddSupplierQuoteDto
{
    [Required, StringLength(100)]
    public string SupplierName { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal Amount { get; set; }

    [Range(0, 3650)]
    public int DeliveryDays { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
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

public class PurchaseRequestDetailDto : PurchaseRequestSummaryDto
{
    public decimal? SelectedQuoteAmount { get; set; }
    public decimal? ExpectedGrossProfit { get; set; }
    public decimal? SelectedCostPremiumToLowest { get; set; }
    public int? SelectedDeliveryDaysComparedWithLowest { get; set; }
    public List<SupplierQuoteDto> Quotes { get; set; } = new();
    public QuoteDecisionAnalysisDto Decision { get; set; } = new();
    public List<ReviewEventDto> History { get; set; } = new();
}


