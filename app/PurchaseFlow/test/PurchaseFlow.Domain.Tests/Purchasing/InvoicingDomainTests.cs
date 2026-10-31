using System;
using System.Collections.Generic;
using System.Linq;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配 + 付款）：发票与付款申请的领域规则。
/// 匹配校验（单价/收货数量/订单归属）需要跨聚合查询，放在应用层；
/// 领域层保证：金额=明细合计、状态只能单向流转、付款金额必须为正。
/// </summary>
public class InvoicingDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_invoice_amount_is_the_sum_of_its_lines_and_starts_matched()
    {
        var invoice = NewInvoice();

        invoice.AddLine(Guid.NewGuid(), Guid.NewGuid(), "办公椅", "人体工学", 10m, 90m);
        invoice.AddLine(Guid.NewGuid(), Guid.NewGuid(), "会议桌", "2.4m", 2m, 150m);

        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(1200m, invoice.Lines.Sum(x => x.Subtotal));
        Assert.Equal(invoice.Lines.Sum(x => x.Subtotal), invoice.Amount);
        Assert.Equal(SupplierInvoiceStatus.Matched, invoice.Status);
    }

    [Fact]
    public void Invoice_lines_need_positive_quantity_and_non_negative_price()
    {
        var invoice = NewInvoice();

        Assert.Equal(PurchaseFlowErrors.InvalidQuantity,
            Assert.Throws<BusinessException>(() =>
                invoice.AddLine(Guid.NewGuid(), Guid.NewGuid(), "办公椅", "", 0m, 90m)).Code);
        Assert.Equal(PurchaseFlowErrors.InvalidUnitPrice,
            Assert.Throws<BusinessException>(() =>
                invoice.AddLine(Guid.NewGuid(), Guid.NewGuid(), "办公椅", "", 1m, -1m)).Code);
    }

    [Fact]
    public void Invoice_status_only_moves_forward()
    {
        var invoice = NewInvoice();
        invoice.AddLine(Guid.NewGuid(), Guid.NewGuid(), "办公椅", "", 1m, 90m);

        invoice.MarkPaymentRequested();
        Assert.Equal(SupplierInvoiceStatus.PaymentRequested, invoice.Status);

        // 已申请付款后不能再重复进入付款申请。
        Assert.Equal(PurchaseFlowErrors.InvalidStatus,
            Assert.Throws<BusinessException>(() => invoice.MarkPaymentRequested()).Code);

        invoice.MarkPaid();
        Assert.Equal(SupplierInvoiceStatus.Paid, invoice.Status);

        Assert.Equal(PurchaseFlowErrors.InvalidStatus,
            Assert.Throws<BusinessException>(() => invoice.MarkPaid()).Code);
    }

    [Fact]
    public void A_payment_application_requires_a_positive_amount()
    {
        Assert.Equal(PurchaseFlowErrors.InvalidPaymentAmount,
            Assert.Throws<BusinessException>(() => NewPayment(0m)).Code);
        Assert.Equal(PurchaseFlowErrors.InvalidPaymentAmount,
            Assert.Throws<BusinessException>(() => NewPayment(-1m)).Code);
    }

    [Fact]
    public void A_payment_must_be_approved_before_it_can_be_paid()
    {
        var payment = NewPayment(900m);

        Assert.Equal(PaymentApplicationStatus.PendingApproval, payment.Status);
        Assert.Equal(PurchaseFlowErrors.PaymentNotApproved,
            Assert.Throws<BusinessException>(() => payment.RegisterPayment(Now, Now.Date)).Code);

        var approver = Guid.NewGuid();
        payment.Approve(approver, Now);

        Assert.Equal(PaymentApplicationStatus.Approved, payment.Status);
        Assert.Equal(approver, payment.ApprovedByUserId);
        Assert.Equal(Now, payment.ApprovedAt);

        // 同一申请不能重复批准。
        Assert.Equal(PurchaseFlowErrors.PaymentNotPending,
            Assert.Throws<BusinessException>(() => payment.Approve(Guid.NewGuid(), Now)).Code);

        payment.RegisterPayment(Now.AddHours(1), Now.Date);
        Assert.Equal(PaymentApplicationStatus.Paid, payment.Status);

        // 已付款后不能再登记一次。
        Assert.Equal(PurchaseFlowErrors.PaymentNotApproved,
            Assert.Throws<BusinessException>(() => payment.RegisterPayment(Now.AddHours(2), Now.Date)).Code);
    }

    private static SupplierInvoice NewInvoice() =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "PO-SO-1", null, "供应商A",
            $"INV-{Guid.NewGuid():N}", Now.Date, null);

    private static PaymentApplication NewPayment(decimal amount) =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "PO-SO-1", Guid.NewGuid(),
            "INV-TEST", amount, null);
}
