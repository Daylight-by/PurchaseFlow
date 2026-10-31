using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// One immutable quantity entry in a goods receipt, linked to an actual PO line.
/// </summary>
public class GoodsReceiptLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid GoodsReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public decimal Quantity { get; private set; }

    protected GoodsReceiptLine() { }

    public GoodsReceiptLine(
        Guid id,
        Guid? tenantId,
        Guid goodsReceiptId,
        Guid purchaseOrderLineId,
        decimal quantity)
        : base(id)
    {
        TenantId = tenantId;
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        Quantity = quantity;
        EnsureQuantity(quantity);
    }

    private static void EnsureQuantity(decimal quantity)
    {
        if (quantity <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
        if (decimal.Round(quantity, 3, MidpointRounding.AwayFromZero) != quantity)
            throw new BusinessException(PurchaseFlowErrors.ReceiptQuantityPrecision);
    }
}
