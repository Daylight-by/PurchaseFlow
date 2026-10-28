using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

public class SupplierQuote : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public string SupplierName { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public int DeliveryDays { get; private set; }
    public string? Note { get; private set; }

    protected SupplierQuote() { }

    public SupplierQuote(Guid id, Guid? tenantId, Guid purchaseRequestId, string supplierName, decimal amount, int deliveryDays, string? note)
        : base(id)
    {
        if (amount <= 0 || deliveryDays < 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);
        TenantId = tenantId;
        PurchaseRequestId = purchaseRequestId;
        SupplierName = Check.NotNullOrWhiteSpace(supplierName, nameof(supplierName), maxLength: 100).Trim();
        Amount = amount;
        DeliveryDays = deliveryDays;
        Note = string.IsNullOrWhiteSpace(note) ? null : Check.Length(note.Trim(), nameof(note), maxLength: 500);
    }
}


