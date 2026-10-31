using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：供应商发票聚合根。发票必须挂在一张已存在的采购订单上，
/// 其明细逐行引用订单行。
///
/// 分层说明：订单/收货/历史发票的“三单匹配”校验需要跨聚合查询，放在应用层完成；
/// 领域层只负责两件不会错的事 —— 发票金额必须等于明细合计、状态只能单向流转
/// （Matched → PaymentRequested → Paid）。这样即便未来新增入口也不会绕过金额铁律。
/// </summary>
public class SupplierInvoice : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>订单号快照：历史发票不随订单改名/删除而丢失可读信息。</summary>
    public string PurchaseOrderNumber { get; private set; } = string.Empty;

    /// <summary>供应商档案 Id（可空，旧订单可能没有关联档案）。</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>供应商名称快照：档案改名不影响历史发票。</summary>
    public string SupplierName { get; private set; } = string.Empty;

    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>发票日期（只保留日期，SQLite 以 TEXT 存储）。</summary>
    public DateTime InvoiceDate { get; private set; }

    /// <summary>发票金额 = 明细小计合计，存储属性，仅由 RecalculateAmount 写入。</summary>
    public decimal Amount { get; private set; }

    public SupplierInvoiceStatus Status { get; private set; }
    public string? Note { get; private set; }

    public ICollection<SupplierInvoiceLine> Lines { get; private set; } = new List<SupplierInvoiceLine>();

    protected SupplierInvoice() { }

    public SupplierInvoice(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string purchaseOrderNumber,
        Guid? supplierId,
        string supplierName,
        string invoiceNumber,
        DateTime invoiceDate,
        string? note)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderNumber = Check.NotNullOrWhiteSpace(purchaseOrderNumber, nameof(purchaseOrderNumber), maxLength: 128).Trim();
        SupplierId = supplierId;
        SupplierName = Check.NotNullOrWhiteSpace(supplierName, nameof(supplierName), maxLength: 100).Trim();
        InvoiceNumber = Check.NotNullOrWhiteSpace(invoiceNumber, nameof(invoiceNumber), maxLength: 64).Trim();
        // 发票日期是业务日期：default(DateTime) 视为非法；只保留日期部分，避免时分秒导致重号/展示歧义。
        if (invoiceDate == default)
            throw new BusinessException(PurchaseFlowErrors.InvalidInvoiceDate);
        InvoiceDate = DateTime.SpecifyKind(invoiceDate.Date, DateTimeKind.Unspecified);
        Note = string.IsNullOrWhiteSpace(note)
            ? null
            : Check.Length(note.Trim(), nameof(note), maxLength: 500);
        // 能保存下来的发票一定通过了三单匹配，因此初始状态是 Matched。
        Status = SupplierInvoiceStatus.Matched;
        Amount = 0m;
    }

    /// <summary>
    /// 追加一条发票明细，并在明细变化后立即重算发票金额，
    /// 保证“发票金额 = 明细合计”永远成立。
    /// 同一条订单行在一张发票里只允许出现一次，否则金额与可开票数量都无法解释。
    /// </summary>
    public SupplierInvoiceLine AddLine(
        Guid id,
        Guid purchaseOrderLineId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
    {
        if (Lines.Any(line => line.PurchaseOrderLineId == purchaseOrderLineId))
            throw new BusinessException(PurchaseFlowErrors.DuplicateLineId);

        var line = new SupplierInvoiceLine(id, TenantId, Id, purchaseOrderLineId, itemName, specification, quantity, unitPrice);
        Lines.Add(line);
        RecalculateAmount();
        return line;
    }

    /// <summary>发票金额 = 明细小计合计；只在明细变化处调用。</summary>
    public void RecalculateAmount()
    {
        Amount = Lines.Sum(x => x.Subtotal);
    }

    /// <summary>
    /// 提交付款申请后调用。只有 Matched 能进入付款流程，防止重复申请/越级流转。
    /// </summary>
    public void MarkPaymentRequested()
    {
        if (Status != SupplierInvoiceStatus.Matched)
            throw new BusinessException(PurchaseFlowErrors.InvalidStatus);
        Status = SupplierInvoiceStatus.PaymentRequested;
    }

    /// <summary>
    /// 登记付款后调用。只有 PaymentRequested（即已有付款申请）才能变为 Paid。
    /// </summary>
    public void MarkPaid()
    {
        if (Status != SupplierInvoiceStatus.PaymentRequested)
            throw new BusinessException(PurchaseFlowErrors.InvalidStatus);
        Status = SupplierInvoiceStatus.Paid;
    }
}
