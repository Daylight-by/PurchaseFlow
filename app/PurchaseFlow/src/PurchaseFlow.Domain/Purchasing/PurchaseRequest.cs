using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

public class PurchaseRequest : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BuyerUserId { get; private set; }
    public string OrderReference { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal SaleAmount { get; private set; }
    public PurchaseRequestStatus Status { get; private set; }
    public Guid? SelectedQuoteId { get; private set; }
    public string? SelectionReason { get; private set; }

    protected PurchaseRequest() { }

    public PurchaseRequest(Guid id, Guid? tenantId, Guid buyerUserId, string orderReference, string description, decimal saleAmount)
        : base(id)
    {
        TenantId = tenantId;
        BuyerUserId = buyerUserId;
        SetDetails(orderReference, description, saleAmount);
        Status = PurchaseRequestStatus.Draft;
    }

    public bool IsEditable => Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned;

    public void SetDetails(string orderReference, string description, decimal saleAmount)
    {
        if (Status is PurchaseRequestStatus.PendingReview or PurchaseRequestStatus.Approved)
            throw new BusinessException(PurchaseFlowErrors.NotEditable);
        if (saleAmount <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);
        OrderReference = Check.NotNullOrWhiteSpace(orderReference, nameof(orderReference), maxLength: 64).Trim();
        Description = Check.NotNullOrWhiteSpace(description, nameof(description), maxLength: 500).Trim();
        SaleAmount = saleAmount;
    }

    public void EnsureEditable()
    {
        if (!IsEditable)
            throw new BusinessException(PurchaseFlowErrors.NotEditable);
    }

    public void SelectQuote(Guid quoteId, string reason)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException(PurchaseFlowErrors.NeedSelectionReason);
        SelectedQuoteId = quoteId;
        SelectionReason = Check.Length(reason.Trim(), nameof(reason), maxLength: 500);
    }

    public void ClearSelectedQuote(Guid quoteId)
    {
        EnsureEditable();
        if (SelectedQuoteId != quoteId) return;
        SelectedQuoteId = null;
        SelectionReason = null;
    }

    public void Submit(int quoteCount)
    {
        EnsureEditable();
        if (quoteCount < 2)
            throw new BusinessException(PurchaseFlowErrors.NeedTwoQuotes);
        if (SelectedQuoteId is null)
            throw new BusinessException(PurchaseFlowErrors.NeedSelectedQuote);
        if (string.IsNullOrWhiteSpace(SelectionReason))
            throw new BusinessException(PurchaseFlowErrors.NeedSelectionReason);
        Status = PurchaseRequestStatus.PendingReview;
    }

    public void Approve()
    {
        EnsurePendingReview();
        Status = PurchaseRequestStatus.Approved;
    }

    public void Return()
    {
        EnsurePendingReview();
        Status = PurchaseRequestStatus.Returned;
    }

    private void EnsurePendingReview()
    {
        if (Status != PurchaseRequestStatus.PendingReview)
            throw new BusinessException(PurchaseFlowErrors.InvalidStatus);
    }
}


