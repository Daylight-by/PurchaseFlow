using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// One ordered item of a purchase order. The line keeps the item data of the
/// approved purchase request, so the order can be sent to the supplier without
/// looking at the request again.
/// </summary>
public class PurchaseOrderLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal ReceivedQuantity { get; private set; }

    public decimal RemainingQuantity => Quantity - ReceivedQuantity;
    public bool IsFullyReceived => RemainingQuantity == 0m;
    public decimal Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    protected PurchaseOrderLine() { }

    public PurchaseOrderLine(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        ItemName = Check.NotNullOrWhiteSpace(itemName, nameof(itemName), maxLength: 100).Trim();
        Specification = string.IsNullOrWhiteSpace(specification)
            ? string.Empty
            : Check.Length(specification.Trim(), nameof(specification), maxLength: 100) ?? string.Empty;
        Quantity = quantity;
        UnitPrice = unitPrice;
        EnsureInvariants();
    }

    public void RegisterReceived(decimal quantity)
    {
        if (quantity <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
        if (decimal.Round(quantity, 3, MidpointRounding.AwayFromZero) != quantity)
            throw new BusinessException(PurchaseFlowErrors.ReceiptQuantityPrecision);
        if (quantity > RemainingQuantity)
            throw new BusinessException(PurchaseFlowErrors.ReceiptExceedsRemaining);

        ReceivedQuantity += quantity;
    }

    public void EnsureInvariants()
    {
        if (Quantity <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
        if (UnitPrice < 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidUnitPrice);
    }
}
