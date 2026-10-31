using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮：付款申请聚合根。业务闭环：匹配发票 → 申请付款（PendingApproval）→
/// 管理员批准（Approved）→ 管理员登记付款（Paid）。
///
/// 职责分离由应用层权限强制（采购员/管理员可发起，只有管理员能批准与登记付款），
/// 领域层保证金额 > 0 且状态只能单向流转，重复/越级操作一律拒绝。
/// </summary>
public class PaymentApplication : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>订单号快照，付款单展示不依赖订单实时数据。</summary>
    public string PurchaseOrderNumber { get; private set; } = string.Empty;

    public Guid SupplierInvoiceId { get; private set; }

    /// <summary>发票号快照。</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public PaymentApplicationStatus Status { get; private set; }
    public string? Note { get; private set; }

    /// <summary>批准人/批准时间：审批留痕，便于审计“谁批准的”。</summary>
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAt { get; private set; }

    /// <summary>实际登记付款的时间与付款日期（业务日期）。</summary>
    public DateTime? PaidAt { get; private set; }
    public DateTime? PaymentDate { get; private set; }

    protected PaymentApplication() { }

    public PaymentApplication(
        Guid id,
        Guid? tenantId,
        Guid purchaseOrderId,
        string purchaseOrderNumber,
        Guid supplierInvoiceId,
        string invoiceNumber,
        decimal amount,
        string? note)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderNumber = Check.NotNullOrWhiteSpace(purchaseOrderNumber, nameof(purchaseOrderNumber), maxLength: 128).Trim();
        SupplierInvoiceId = supplierInvoiceId;
        InvoiceNumber = Check.NotNullOrWhiteSpace(invoiceNumber, nameof(invoiceNumber), maxLength: 64).Trim();
        // 付款金额必须为正：0/负数没有业务含义，也不允许用负数反向冲账。
        if (amount <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidPaymentAmount);
        Amount = amount;
        Note = string.IsNullOrWhiteSpace(note)
            ? null
            : Check.Length(note.Trim(), nameof(note), maxLength: 500);
        Status = PaymentApplicationStatus.PendingApproval;
    }

    /// <summary>
    /// 管理员批准。只有 PendingApproval 可批准，重复批准/越级登记付款都报 PaymentNotPending。
    /// </summary>
    public void Approve(Guid approverUserId, DateTime nowUtc)
    {
        if (Status != PaymentApplicationStatus.PendingApproval)
            throw new BusinessException(PurchaseFlowErrors.PaymentNotPending);
        ApprovedByUserId = approverUserId;
        ApprovedAt = nowUtc;
        Status = PaymentApplicationStatus.Approved;
    }

    /// <summary>
    /// 管理员登记付款。只有 Approved 可登记，未批准直接登记报 PaymentNotApproved；
    /// 重复登记同样拒绝（状态已不是 Approved）。
    /// </summary>
    public void RegisterPayment(DateTime nowUtc, DateTime paymentDate)
    {
        if (Status != PaymentApplicationStatus.Approved)
            throw new BusinessException(PurchaseFlowErrors.PaymentNotApproved);
        PaidAt = nowUtc;
        PaymentDate = DateTime.SpecifyKind(paymentDate.Date, DateTimeKind.Unspecified);
        Status = PaymentApplicationStatus.Paid;
    }
}
