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

    /// <summary>
    /// 第二轮第 3 批：来源物料档案 Id（可空，旧订单行为 null）。用于按物料追溯
    /// 历史采购成交价；刻意不建外键 —— 物料档案可停用/更名，历史订单优先。
    /// </summary>
    public Guid? MaterialId { get; private set; }

    public string ItemName { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal ReceivedQuantity { get; private set; }

    public decimal RemainingQuantity => Quantity - ReceivedQuantity;
    public bool IsFullyReceived => RemainingQuantity == 0m;
    public decimal Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    protected PurchaseOrderLine() { }

    /// <summary>
    /// 旧签名保留：不关联物料档案（materialId=null），委托给带 materialId 的新重载，
    /// 保证既有调用/旧测试不受影响。
    /// </summary>
    public PurchaseOrderLine(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
        : this(id, tenantId, purchaseOrderId, itemName, specification, quantity, unitPrice, materialId: null)
    {
    }

    public PurchaseOrderLine(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice,
        Guid? materialId)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        MaterialId = materialId;
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
