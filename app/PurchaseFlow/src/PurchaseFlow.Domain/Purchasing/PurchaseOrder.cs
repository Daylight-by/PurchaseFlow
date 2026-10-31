using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// A purchase order that is created when a purchase request is approved. It
/// copies the approved lines and the selected quote, so the order can be sent
/// to the supplier without any further decision.
/// </summary>
public class PurchaseOrder : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public Guid PurchaseRequestId { get; private set; }

    /// <summary>
    /// 第二轮主数据：订单来源供应商档案 Id（快照自选中报价，可空）。
    /// SupplierName 仍是名称快照，档案改名不影响历史订单；刻意不建外键。
    /// </summary>
    public Guid? SupplierId { get; private set; }

    public string SupplierName { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public int DeliveryDays { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public int ReceiptVersion { get; private set; }

    public ICollection<PurchaseOrderLine> Lines { get; private set; } = new List<PurchaseOrderLine>();

    protected PurchaseOrder() { }

    public PurchaseOrder(
        Guid id,
        Guid? tenantId,
        string orderNumber,
        Guid purchaseRequestId,
        string supplierName,
        decimal amount,
        int deliveryDays,
        Guid? supplierId = null)
        : base(id)
    {
        TenantId = tenantId;
        // P1-1：单号是 "PO-" + OrderReference（OrderReference 上限 64），
        // "PO-" + 64 字符最长 67，原 64 上限会把合法请求挡在领域校验里。
        // 因此单号上限扩到 128，OrderReference 上限维持 64 不变。
        OrderNumber = Check.NotNullOrWhiteSpace(orderNumber, nameof(orderNumber), maxLength: 128).Trim();
        PurchaseRequestId = purchaseRequestId;
        // 第二轮主数据：supplierId 放最后且可选，旧调用（不关联档案）不用改。
        SupplierId = supplierId;
        SupplierName = Check.NotNullOrWhiteSpace(supplierName, nameof(supplierName), maxLength: 100).Trim();
        // amount 参数保留：历史/迁移数据物化时仍需能带入既有金额做兼容。
        // 新订单的金额以明细合计为准，任何 AddLine 都会调用
        // RecalculateAmountFromLines() 覆盖这里的初始值。
        Amount = amount;
        DeliveryDays = deliveryDays;
        Status = PurchaseOrderStatus.Created;
    }

    /// <summary>
    /// 订单金额 = 订单明细小计合计。P0-1：明细使用所选报价明细的成交单价，
    /// 因此订单金额必须由这些明细汇总，不能沿用报价总额或需求预算金额。
    /// </summary>
    public void RecalculateAmountFromLines()
    {
        Amount = Lines.Sum(x => x.Subtotal);
    }

    /// <summary>
    /// Adds an ordered line. The order keeps its own lines, the amount of the
    /// order comes from the selected quote of the request.
    /// </summary>
    public PurchaseOrderLine AddLine(Guid id, string itemName, string specification, decimal quantity, decimal unitPrice)
    {
        var line = new PurchaseOrderLine(id, TenantId, Id, itemName, specification, quantity, unitPrice);
        Lines.Add(line);
        // 明细变化后立即重算，保证落库的订单金额始终等于明细合计。
        RecalculateAmountFromLines();
        return line;
    }

    public void RegisterReceipt(
        IReadOnlyCollection<PurchaseOrderLine> lines,
        IReadOnlyDictionary<Guid, decimal> quantities)
    {
        if (Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed)
            throw new BusinessException(PurchaseFlowErrors.ReceiptOrderNotReceivable);
        if (Status is not (PurchaseOrderStatus.Created or PurchaseOrderStatus.Issued))
            throw new BusinessException(PurchaseFlowErrors.ReceiptOrderNotReceivable);
        if (lines.Count == 0 || quantities.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidReceiptLines);

        var linesById = lines
            .Where(line => line.PurchaseOrderId == Id)
            .GroupBy(line => line.Id)
            .ToDictionary(group => group.Key, group => group.Single());
        if (linesById.Count != lines.Count || quantities.Keys.Any(id => !linesById.ContainsKey(id)))
            throw new BusinessException(PurchaseFlowErrors.ReceiptLineNotInOrder);

        foreach (var (lineId, quantity) in quantities)
            linesById[lineId].RegisterReceived(quantity);

        if (linesById.Values.All(line => line.IsFullyReceived))
            Status = PurchaseOrderStatus.Received;

        ReceiptVersion++;
    }

    public void Issue()
    {
        if (Status != PurchaseOrderStatus.Created)
            throw new BusinessException(PurchaseFlowErrors.InvalidStatus);
        Status = PurchaseOrderStatus.Issued;
    }
}
