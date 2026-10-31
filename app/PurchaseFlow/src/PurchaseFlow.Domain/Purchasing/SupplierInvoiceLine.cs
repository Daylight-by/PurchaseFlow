using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：一条供应商发票明细，对应采购订单的一行成交记录。
/// 物料名称/规格取订单行快照；数量与单价由应用层做过“订单单价一致、数量不超过
/// 已收货未开票数量”的匹配校验后才允许写入，本实体只守住自身字段不变式。
/// </summary>
public class SupplierInvoiceLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SupplierInvoiceId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>小计与订单行同口径：数量 × 单价，AwayFromZero 四舍五入到 2 位。</summary>
    public decimal Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    protected SupplierInvoiceLine() { }

    public SupplierInvoiceLine(
        Guid id,
        Guid? tenantId,
        Guid supplierInvoiceId,
        Guid purchaseOrderLineId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        TenantId = tenantId;
        SupplierInvoiceId = supplierInvoiceId;
        PurchaseOrderLineId = purchaseOrderLineId;
        // 与订单行相同的字段校验口径：物料名必填≤100、规格≤100、数量>0、单价>=0。
        ItemName = Check.NotNullOrWhiteSpace(itemName, nameof(itemName), maxLength: 100).Trim();
        Specification = string.IsNullOrWhiteSpace(specification)
            ? string.Empty
            : Check.Length(specification.Trim(), nameof(specification), maxLength: 100) ?? string.Empty;
        Quantity = quantity;
        UnitPrice = unitPrice;
        EnsureInvariants();
    }

    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(ItemName))
            throw new BusinessException(PurchaseFlowErrors.InvalidItemName);
        if (Quantity <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
        if (UnitPrice < 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidUnitPrice);
    }
}
