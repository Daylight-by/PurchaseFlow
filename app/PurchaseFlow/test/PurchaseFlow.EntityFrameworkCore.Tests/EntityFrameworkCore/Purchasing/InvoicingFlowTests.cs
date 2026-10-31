using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第三轮（三单匹配 + 付款）的端到端集成测试：
/// 订单 → 收货 → 发票匹配（单价/已收数量）→ 发票 Matched → 付款申请 → 管理员批准 → 登记付款；
/// 以及各类不匹配/状态越权的拒绝路径。
/// </summary>
[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class InvoicingFlowTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _requests;
    private readonly IGoodsReceiptAppService _receipts;
    private readonly ISupplierInvoiceAppService _invoices;
    private readonly IPaymentAppService _payments;

    public InvoicingFlowTests()
    {
        _requests = GetRequiredService<IPurchaseRequestAppService>();
        _receipts = GetRequiredService<IGoodsReceiptAppService>();
        _invoices = GetRequiredService<ISupplierInvoiceAppService>();
        _payments = GetRequiredService<IPaymentAppService>();
    }

    [Fact]
    public async Task A_received_order_invoice_matches_then_flows_through_payment_to_paid()
    {
        var order = await CreateReceivedOrderAsync(receiveQuantity: 6m);

        var invoice = await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(new CreateSupplierInvoiceDto
        {
            PurchaseOrderId = order.OrderId,
            InvoiceNumber = "INV-FULL-001",
            InvoiceDate = DateTime.UtcNow.Date,
            Note = "三单匹配测试发票",
            Lines = new List<CreateSupplierInvoiceLineDto>
            {
                new() { PurchaseOrderLineId = order.OrderLineId, Quantity = 6m, UnitPrice = 90m }
            }
        }));

        Assert.Equal(SupplierInvoiceStatus.Matched, invoice.Status);
        Assert.Equal(540m, invoice.Amount);
        Assert.Equal(invoice.Lines.Sum(x => x.Subtotal), invoice.Amount);
        Assert.Equal(order.OrderNumber, invoice.PurchaseOrderNumber);

        var payment = await WithUnitOfWorkAsync(() => _payments.RequestAsync(invoice.Id,
            new CreatePaymentApplicationDto { Note = "请安排付款" }));
        Assert.Equal(PaymentApplicationStatus.PendingApproval, payment.Status);
        Assert.Equal(540m, payment.Amount);

        var invoiceAfterRequest = await WithUnitOfWorkAsync(() => _invoices.GetAsync(invoice.Id));
        Assert.Equal(SupplierInvoiceStatus.PaymentRequested, invoiceAfterRequest.Status);
        Assert.Equal(payment.Id, invoiceAfterRequest.PaymentId);
        Assert.Equal(PaymentApplicationStatus.PendingApproval, invoiceAfterRequest.PaymentStatus);

        var approved = await WithUnitOfWorkAsync(() => _payments.ApproveAsync(payment.Id,
            new ReviewPaymentApplicationDto { Comment = "同意付款" }));
        Assert.Equal(PaymentApplicationStatus.Approved, approved.Status);

        var paid = await WithUnitOfWorkAsync(() => _payments.RegisterPaymentAsync(payment.Id,
            new RegisterPaymentDto { PaymentDate = DateTime.UtcNow.Date, Note = "银行转账" }));
        Assert.Equal(PaymentApplicationStatus.Paid, paid.Status);
        Assert.NotNull(paid.PaidAt);

        var invoiceAfterPayment = await WithUnitOfWorkAsync(() => _invoices.GetAsync(invoice.Id));
        Assert.Equal(SupplierInvoiceStatus.Paid, invoiceAfterPayment.Status);
        Assert.Equal(PaymentApplicationStatus.Paid, invoiceAfterPayment.PaymentStatus);
    }

    [Fact]
    public async Task Invoices_that_do_not_match_the_order_or_received_quantity_are_rejected()
    {
        var order = await CreateReceivedOrderAsync(receiveQuantity: 6m);

        // 单价与订单成交单价不一致。
        var priceMismatch = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-MISMATCH-PRICE", 1m, 91m))));
        Assert.Equal(PurchaseFlowErrors.InvoicePriceMismatch, priceMismatch.Code);

        // 超过已收货数量。
        var quantityExceeds = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-MISMATCH-QTY", 7m, 90m))));
        Assert.Equal(PurchaseFlowErrors.InvoiceQuantityExceedsReceived, quantityExceeds.Code);

        // 订单行不属于当前订单。
        var foreign = new CreateSupplierInvoiceDto
        {
            PurchaseOrderId = order.OrderId,
            InvoiceNumber = "INV-MISMATCH-LINE",
            InvoiceDate = DateTime.UtcNow.Date,
            Lines = new List<CreateSupplierInvoiceLineDto>
            {
                new() { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 1m, UnitPrice = 90m }
            }
        };
        var foreignLine = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(foreign)));
        Assert.Equal(PurchaseFlowErrors.InvoiceLineNotInOrder, foreignLine.Code);

        // 登记一张合法发票后，同订单同发票号不能重复。
        await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-DUP-001", 1m, 90m)));
        var duplicateNumber = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-DUP-001", 1m, 90m))));
        Assert.Equal(PurchaseFlowErrors.InvoiceNumberDuplicate, duplicateNumber.Code);
    }

    [Fact]
    public async Task An_invoice_needs_received_goods_before_it_can_be_registered()
    {
        var order = await CreateApprovedOrderAsync(receiveQuantity: 0m);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-NO-RECEIPT", 1m, 90m))));
        Assert.Equal(PurchaseFlowErrors.InvoiceNeedsReceivedGoods, exception.Code);
    }

    [Fact]
    public async Task Cumulative_invoiced_quantity_can_not_exceed_received_quantity()
    {
        var order = await CreateReceivedOrderAsync(receiveQuantity: 6m);

        await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-PART-1", 4m, 90m)));
        var second = await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-PART-2", 2m, 90m)));
        Assert.Equal(180m, second.Amount);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-PART-3", 1m, 90m))));
        Assert.Equal(PurchaseFlowErrors.InvoiceQuantityExceedsReceived, exception.Code);
    }

    [Fact]
    public async Task Payment_amount_and_status_rules_are_enforced()
    {
        var order = await CreateReceivedOrderAsync(receiveQuantity: 6m);
        var invoice = await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-PAY-RULES", 6m, 90m)));

        // 超过发票金额。
        var exceeds = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _payments.RequestAsync(invoice.Id,
                new CreatePaymentApplicationDto { Amount = 600m })));
        Assert.Equal(PurchaseFlowErrors.PaymentAmountExceedsInvoice, exceeds.Code);

        var payment = await WithUnitOfWorkAsync(() => _payments.RequestAsync(invoice.Id,
            new CreatePaymentApplicationDto()));

        // 同一发票不能重复申请。
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _payments.RequestAsync(invoice.Id,
                new CreatePaymentApplicationDto())));
        Assert.Equal(PurchaseFlowErrors.DuplicatePaymentForInvoice, duplicate.Code);

        // 未批准不能登记付款。
        var notApproved = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _payments.RegisterPaymentAsync(payment.Id,
                new RegisterPaymentDto { PaymentDate = DateTime.UtcNow.Date })));
        Assert.Equal(PurchaseFlowErrors.PaymentNotApproved, notApproved.Code);

        await WithUnitOfWorkAsync(() => _payments.ApproveAsync(payment.Id, new ReviewPaymentApplicationDto()));
        await WithUnitOfWorkAsync(() => _payments.RegisterPaymentAsync(payment.Id,
            new RegisterPaymentDto { PaymentDate = DateTime.UtcNow.Date }));

        // 已付款后不能重复登记或重复批准。
        var alreadyPaid = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _payments.RegisterPaymentAsync(payment.Id,
                new RegisterPaymentDto { PaymentDate = DateTime.UtcNow.Date })));
        Assert.Equal(PurchaseFlowErrors.PaymentNotApproved, alreadyPaid.Code);

        var alreadyApproved = await Assert.ThrowsAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _payments.ApproveAsync(payment.Id, new ReviewPaymentApplicationDto())));
        Assert.Equal(PurchaseFlowErrors.PaymentNotPending, alreadyApproved.Code);
    }

    [Fact]
    public async Task Order_invoice_list_reports_match_and_payment_state()
    {
        var order = await CreateReceivedOrderAsync(receiveQuantity: 6m);
        var invoice = await WithUnitOfWorkAsync(() => _invoices.RegisterAsync(Invoice(order, "INV-LIST-001", 3m, 90m)));
        await WithUnitOfWorkAsync(() => _payments.RequestAsync(invoice.Id, new CreatePaymentApplicationDto()));

        var list = await WithUnitOfWorkAsync(() => _invoices.GetOrderInvoicesAsync(order.OrderId));
        var item = Assert.Single(list);
        Assert.Equal("INV-LIST-001", item.InvoiceNumber);
        Assert.Equal(270m, item.Amount);
        Assert.Equal(SupplierInvoiceStatus.PaymentRequested, item.Status);
        Assert.Equal(PaymentApplicationStatus.PendingApproval, item.PaymentStatus);
        Assert.NotNull(item.PaymentId);
    }

    private static CreateSupplierInvoiceDto Invoice(
        (Guid OrderId, Guid OrderLineId, string OrderNumber) order,
        string invoiceNumber,
        decimal quantity,
        decimal unitPrice) => new()
    {
        PurchaseOrderId = order.OrderId,
        InvoiceNumber = invoiceNumber,
        InvoiceDate = DateTime.UtcNow.Date,
        Lines = new List<CreateSupplierInvoiceLineDto>
        {
            new() { PurchaseOrderLineId = order.OrderLineId, Quantity = quantity, UnitPrice = unitPrice }
        }
    };

    private async Task<(Guid OrderId, Guid OrderLineId, string OrderNumber)> CreateReceivedOrderAsync(decimal receiveQuantity)
    {
        var order = await CreateApprovedOrderAsync(receiveQuantity);
        if (receiveQuantity > 0)
        {
            await WithUnitOfWorkAsync(() => _receipts.CreateAsync(new CreateGoodsReceiptDto
            {
                PurchaseOrderId = order.OrderId,
                IdempotencyKey = $"inv-receipt-{Guid.NewGuid():N}",
                ReceiptDate = DateTime.UtcNow.Date,
                Note = "三单匹配测试收货",
                Lines = new List<GoodsReceiptLineInputDto>
                {
                    new() { PurchaseOrderLineId = order.OrderLineId, Quantity = receiveQuantity }
                }
            }));
        }

        return order;
    }

    private async Task<(Guid OrderId, Guid OrderLineId, string OrderNumber)> CreateApprovedOrderAsync(decimal receiveQuantity)
    {
        var requestId = await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"INV-{Guid.NewGuid():N}",
            Description = "三单匹配测试订单",
            SaleAmount = 10000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "办公椅", Specification = "人体工学", Quantity = 10m, UnitPrice = 100m }
            }
        }));
        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var line = detail.Lines.Single();

        var quoteId = await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierName = "发票测试供应商",
            DeliveryDays = 5,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = line.Id, UnitPrice = 90m }
            }
        }));
        await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierName = "备选供应商",
            DeliveryDays = 7,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = line.Id, UnitPrice = 95m }
            }
        }));
        await WithUnitOfWorkAsync(() => _requests.SelectQuoteAsync(requestId, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = "价格更低"
        }));
        await WithUnitOfWorkAsync(() => _requests.SubmitAsync(requestId));
        await WithUnitOfWorkAsync(() => _requests.ApproveAsync(requestId, new ReviewPurchaseRequestDto
        {
            Comment = "低金额一级批准"
        }));

        var approved = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);
        return (order.Id, order.Lines.Single().Id, order.OrderNumber);
    }
}
