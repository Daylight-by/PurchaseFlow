using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮：付款申请应用服务。状态机：Matched 发票 → 申请付款（PendingApproval）→
/// 管理员批准（Approved）→ 管理员登记付款（Paid，发票同步 Paid）。
///
/// 职责分离：申请由采购员/管理员发起（RegisterInvoices），批准与登记付款只给管理员
/// （ApprovePayments）；状态流转的非法跳转由领域层拒绝，应用层只做跨聚合校验。
/// </summary>
[Authorize(PurchaseFlowPermissions.ViewInvoices)]
public class PaymentAppService : ApplicationService, IPaymentAppService
{
    private readonly IRepository<PaymentApplication, Guid> _payments;
    private readonly IRepository<SupplierInvoice, Guid> _invoices;

    public PaymentAppService(
        IRepository<PaymentApplication, Guid> payments,
        IRepository<SupplierInvoice, Guid> invoices)
    {
        _payments = payments;
        _invoices = invoices;
    }

    public async Task<PaymentListDto> GetListAsync(PaymentListInput input)
    {
        var query = await _payments.GetQueryableAsync();
        query = query.Where(x => x.TenantId == CurrentTenant.Id);

        if (input.Status.HasValue)
            query = query.Where(x => x.Status == input.Status.Value);

        var result = new PaymentListDto { PageSize = Math.Clamp(input.PageSize, 1, 200) };
        result.TotalCount = await AsyncExecuter.CountAsync(query);
        var lastPage = Math.Max(1, (result.TotalCount + result.PageSize - 1) / result.PageSize);
        result.Page = Math.Clamp(input.Page, 1, lastPage);
        var payments = await AsyncExecuter.ToListAsync(query
            .OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id)
            .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize));
        result.Items = payments.Select(Map).ToList();
        return result;
    }

    /// <summary>
    /// 对匹配发票发起付款申请。只有 Matched 状态可申请（一张发票一次）；
    /// 金额为空表示全额，否则必须 >0 且不超过发票金额。
    /// </summary>
    [Authorize(PurchaseFlowPermissions.RegisterInvoices)]
    public async Task<PaymentApplicationDto> RequestAsync(Guid invoiceId, CreatePaymentApplicationDto input)
    {
        var invoice = await _invoices.FindAsync(invoiceId)
            ?? throw new EntityNotFoundException(typeof(SupplierInvoice), invoiceId);

        // 先查是否已有付款申请，再判发票状态：
        // 首次申请后发票会变成 PaymentRequested，如果先判“必须 Matched”，重复申请会
        // 得到 PaymentNotAllowedForInvoice，把真正的“同一发票只能一笔付款申请”语义盖掉。
        // 因此重复申请优先报 DuplicatePaymentForInvoice（DB 唯一索引仍作并发兜底）。
        var paymentQuery = await _payments.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(paymentQuery.Where(x =>
                x.TenantId == CurrentTenant.Id && x.SupplierInvoiceId == invoice.Id)))
            throw new BusinessException(PurchaseFlowErrors.DuplicatePaymentForInvoice);

        if (invoice.Status != SupplierInvoiceStatus.Matched)
            throw new BusinessException(PurchaseFlowErrors.PaymentNotAllowedForInvoice);

        // 申请金额：null=全额；显式金额必须为正且不超过发票金额。
        var amount = input.Amount ?? invoice.Amount;
        if (amount <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidPaymentAmount);
        if (amount > invoice.Amount)
            throw new BusinessException(PurchaseFlowErrors.PaymentAmountExceedsInvoice);

        var payment = new PaymentApplication(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            invoice.PurchaseOrderId,
            invoice.PurchaseOrderNumber,
            invoice.Id,
            invoice.InvoiceNumber,
            amount,
            input.Note);

        invoice.MarkPaymentRequested();
        await _invoices.UpdateAsync(invoice);
        await _payments.InsertAsync(payment);
        return Map(payment);
    }

    /// <summary>
    /// 管理员批准付款申请：仅 PendingApproval → Approved，并留下审批人/时间。
    /// </summary>
    [Authorize(PurchaseFlowPermissions.ApprovePayments)]
    public async Task<PaymentApplicationDto> ApproveAsync(Guid paymentId, ReviewPaymentApplicationDto input)
    {
        var payment = await _payments.FindAsync(paymentId)
            ?? throw new EntityNotFoundException(typeof(PaymentApplication), paymentId);
        // 仅记录状态与审批留痕；审批意见当前不单独落库（无审计表需求，避免超出本轮范围）。
        payment.Approve(UserId(), DateTime.UtcNow);
        await _payments.UpdateAsync(payment);
        return Map(payment);
    }

    /// <summary>
    /// 管理员登记付款：仅 Approved → Paid，同时把发票置为 Paid。
    /// 付款日期可选，默认今天；重复登记会由领域按状态拒绝（PaymentNotApproved）。
    /// </summary>
    [Authorize(PurchaseFlowPermissions.ApprovePayments)]
    public async Task<PaymentApplicationDto> RegisterPaymentAsync(Guid paymentId, RegisterPaymentDto input)
    {
        var payment = await _payments.FindAsync(paymentId)
            ?? throw new EntityNotFoundException(typeof(PaymentApplication), paymentId);

        var invoice = await _invoices.GetAsync(payment.SupplierInvoiceId);
        var paymentDate = input.PaymentDate == default ? DateTime.UtcNow.Date : input.PaymentDate.Date;

        payment.RegisterPayment(DateTime.UtcNow, paymentDate);
        invoice.MarkPaid();

        await _payments.UpdateAsync(payment);
        await _invoices.UpdateAsync(invoice);
        return Map(payment);
    }

    private Guid UserId() => CurrentUser.Id ?? throw new AbpAuthorizationException();

    private static PaymentApplicationDto Map(PaymentApplication payment) => new()
    {
        Id = payment.Id,
        SupplierInvoiceId = payment.SupplierInvoiceId,
        InvoiceNumber = payment.InvoiceNumber,
        PurchaseOrderId = payment.PurchaseOrderId,
        PurchaseOrderNumber = payment.PurchaseOrderNumber,
        Amount = payment.Amount,
        Status = payment.Status,
        Note = payment.Note,
        CreationTime = payment.CreationTime,
        ApprovedByUserId = payment.ApprovedByUserId,
        ApprovedAt = payment.ApprovedAt,
        PaidAt = payment.PaidAt,
        PaymentDate = payment.PaymentDate
    };
}
