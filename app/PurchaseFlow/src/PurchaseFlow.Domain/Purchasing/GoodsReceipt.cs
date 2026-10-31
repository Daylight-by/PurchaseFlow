using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// An immutable receipt document for one purchase order. The application layer
/// validates the order and its lines before inserting this aggregate.
/// </summary>
public class GoodsReceipt : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime ReceiptDate { get; private set; }
    public string? Note { get; private set; }

    public ICollection<GoodsReceiptLine> Lines { get; private set; } = new List<GoodsReceiptLine>();

    protected GoodsReceipt() { }

    public GoodsReceipt(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string idempotencyKey,
        string requestFingerprint,
        DateTime receiptDate,
        string? note)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        IdempotencyKey = Check.NotNullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey), maxLength: 128).Trim();
        RequestFingerprint = Check.NotNullOrWhiteSpace(requestFingerprint, nameof(requestFingerprint), maxLength: 64).Trim();
        ReceiptDate = DateTime.SpecifyKind(receiptDate.Date, DateTimeKind.Unspecified);
        Note = string.IsNullOrWhiteSpace(note)
            ? null
            : Check.Length(note.Trim(), nameof(note), maxLength: 500);
    }

    public GoodsReceiptLine AddLine(Guid id, Guid purchaseOrderLineId, decimal quantity)
    {
        if (Lines.Any(line => line.PurchaseOrderLineId == purchaseOrderLineId))
            throw new BusinessException(PurchaseFlowErrors.DuplicateReceiptLine);

        var line = new GoodsReceiptLine(id, TenantId, Id, purchaseOrderLineId, quantity);
        Lines.Add(line);
        return line;
    }

    public bool HasSameRequest(string fingerprint) =>
        string.Equals(RequestFingerprint, fingerprint, StringComparison.Ordinal);
}
