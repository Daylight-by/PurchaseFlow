using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// A single purchased item inside a <see cref="PurchaseRequest"/> aggregate.
/// The line owns its own invariants; the aggregate root decides when lines may
/// be added, replaced or removed, and it derives the total amount from them.
/// </summary>
public class PurchaseRequestLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// Line total. Money is rounded to two decimals so the row always matches
    /// the amount printed on the purchase order.
    /// </summary>
    public decimal Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    protected PurchaseRequestLine() { }

    public PurchaseRequestLine(
        Guid id,
        Guid? tenantId,
        Guid purchaseRequestId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseRequestId = purchaseRequestId;
        ItemName = Check.NotNullOrWhiteSpace(itemName, nameof(itemName), maxLength: 100).Trim();
        Specification = string.IsNullOrWhiteSpace(specification)
            ? string.Empty
            : Check.Length(specification.Trim(), nameof(specification), maxLength: 100) ?? string.Empty;
        Quantity = quantity;
        UnitPrice = unitPrice;
        EnsureInvariants();
    }

    /// <summary>
    /// P1-3：更新已有明细（而不是删除后重建）。按构造时的同一口径重新赋值并
    /// 校验，保证行 Id 与 EF 跟踪保持稳定；更新字段的口径必须与构造保持一致
    /// （ItemName trim + 长度 Check、Specification 归一化、数量/单价不变式）。
    /// </summary>
    public void Update(string itemName, string specification, decimal quantity, decimal unitPrice)
    {
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
