using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// One itemized line of a supplier quote. P0-1：报价从“一个手填总额”改为
/// “按采购需求明细逐项报价”，因此每条报价都需要自己的明细行来承载
/// “该供应商对某条需求明细的成交单价”。
///
/// 本实体与 <see cref="PurchaseOrderLine"/> 同构：物料 / 规格 / 数量是报价时对
/// 需求明细的快照（报价之后需求可能被改，历史报价必须保持可解释），
/// 小计由数量 × 报价单价计算，绝不接受客户端传入。
/// </summary>
public class SupplierQuoteLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SupplierQuoteId { get; private set; }

    /// <summary>
    /// 来源采购需求明细 Id。只做溯源，不建外键：需求明细允许被整体替换，
    /// 而历史报价必须保留当时的快照，不能因为需求行被删而级联丢失。
    /// </summary>
    public Guid? PurchaseRequestLineId { get; private set; }

    public string ItemName { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// 小计与 <see cref="PurchaseOrderLine"/> 采用完全相同的口径：数量 × 单价，
    /// 四舍五入（AwayFromZero）到 2 位，保证报价合计与订单明细合计逐分一致。
    /// </summary>
    public decimal Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    protected SupplierQuoteLine() { }

    public SupplierQuoteLine(
        Guid id,
        Guid? tenantId,
        Guid supplierQuoteId,
        Guid? purchaseRequestLineId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        TenantId = tenantId;
        SupplierQuoteId = supplierQuoteId;
        PurchaseRequestLineId = purchaseRequestLineId;
        // 与 PurchaseOrderLine 相同的字段校验口径：物料名必填且 ≤100、
        // 规格 ≤100，数量 >0、单价 >=0，错误码复用既有 Invalid*。
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
