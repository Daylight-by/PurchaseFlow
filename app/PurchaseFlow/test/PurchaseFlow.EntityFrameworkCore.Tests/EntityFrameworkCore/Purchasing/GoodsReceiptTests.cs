using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Validation;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class GoodsReceiptTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _requestService;
    private readonly IGoodsReceiptAppService _receiptService;

    public GoodsReceiptTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
    }

    [Fact]
    public async Task A_purchase_order_can_be_received_in_multiple_batches()
    {
        var order = await CreateOrderAsync(("A", 10m), ("B", 5m));
        var first = await ReceiveAsync(order.Id, "batch-1", (Line(order, "A"), 4m), (Line(order, "B"), 2m));

        Assert.True(first.Id != Guid.Empty);
        var partial = await GetReceiptStateAsync(order.Id);
        Assert.True(partial.IsPartiallyReceived);
        Assert.False(partial.IsFullyReceived);
        Assert.Equal(6m, partial.Lines.Single(x => x.ItemName == "A").RemainingQuantity);
        Assert.Equal(3m, partial.Lines.Single(x => x.ItemName == "B").RemainingQuantity);

        await ReceiveAsync(order.Id, "batch-2", (Line(order, "A"), 6m), (Line(order, "B"), 3m));

        var complete = await GetReceiptStateAsync(order.Id);
        Assert.True(complete.IsFullyReceived);
        Assert.Equal(PurchaseOrderStatus.Received, complete.OrderStatus);
        Assert.All(complete.Lines, line => Assert.Equal(0m, line.RemainingQuantity));
        Assert.Equal(2, complete.Receipts.Count);
    }

    [Fact]
    public async Task A_mixed_valid_and_over_receipt_rolls_back_every_line()
    {
        var order = await CreateOrderAsync(("A", 10m), ("B", 5m));

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "over-one-line", (Line(order, "A"), 4m), (Line(order, "B"), 6m)));

        Assert.Equal(PurchaseFlowErrors.ReceiptExceedsRemaining, exception.Code);
        var state = await GetReceiptStateAsync(order.Id);
        Assert.Empty(state.Receipts);
        Assert.All(state.Lines, line => Assert.Equal(0m, line.ReceivedQuantity));
    }

    [Fact]
    public async Task A_receipt_cannot_exceed_the_remaining_quantity_or_precision()
    {
        var order = await CreateOrderAsync(("A", 10m));
        await ReceiveAsync(order.Id, "first", (Line(order, "A"), 4m));

        var over = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "over", (Line(order, "A"), 7m)));
        Assert.Equal(PurchaseFlowErrors.ReceiptExceedsRemaining, over.Code);

        var precision = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "precision", (Line(order, "A"), 1.0005m)));
        Assert.Equal(PurchaseFlowErrors.ReceiptQuantityPrecision, precision.Code);

        var state = await GetReceiptStateAsync(order.Id);
        Assert.Single(state.Receipts);
        Assert.Equal(6m, state.Lines.Single().RemainingQuantity);
    }

    [Fact]
    public async Task Idempotent_retry_returns_the_existing_receipt_and_conflicting_content_fails()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var lineId = Line(order, "A");
        var first = await ReceiveAsync(order.Id, "same-key", (lineId, 4m));
        var retry = await ReceiveAsync(order.Id, "same-key", (lineId, 4m));

        Assert.Equal(first.Id, retry.Id);
        var conflict = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "same-key", (lineId, 5m)));
        Assert.Equal(PurchaseFlowErrors.ReceiptIdempotencyConflict, conflict.Code);

        var state = await GetReceiptStateAsync(order.Id);
        Assert.Single(state.Receipts);
        Assert.Equal(6m, state.Lines.Single().RemainingQuantity);
    }

    [Fact]
    public async Task Empty_zero_and_duplicate_receipt_lines_are_rejected()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var lineId = Line(order, "A");

        await Assert.ThrowsAsync<AbpValidationException>(() => ReceiveAsync(order.Id, "empty"));

        await Assert.ThrowsAsync<AbpValidationException>(() =>
            ReceiveAsync(order.Id, "zero", (lineId, 0m)));

        var duplicate = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "duplicate", (lineId, 1m), (lineId, 1m)));
        Assert.Equal(PurchaseFlowErrors.DuplicateReceiptLine, duplicate.Code);
    }

    [Fact]
    public async Task A_fully_received_order_rejects_new_receipts()
    {
        var order = await CreateOrderAsync(("A", 2m));
        var lineId = Line(order, "A");
        await ReceiveAsync(order.Id, "complete", (lineId, 2m));

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "after-complete", (lineId, 1m)));
        Assert.Equal(PurchaseFlowErrors.ReceiptOrderNotReceivable, exception.Code);
    }

    [Fact]
    public async Task A_closed_order_rejects_new_receipts()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var lineId = Line(order, "A");

        // The application has no close action, so the closed state is written
        // directly to the database to cover the guard of the domain rule.
        await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            await context.PurchaseOrders
                .Where(x => x.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PurchaseOrderStatus.Closed));
        });

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(order.Id, "closed-order", (lineId, 4m)));
        Assert.Equal(PurchaseFlowErrors.ReceiptOrderNotReceivable, exception.Code);

        var state = await GetReceiptStateAsync(order.Id);
        Assert.Empty(state.Receipts);
    }

    [Fact]
    public async Task Receiving_does_not_change_order_or_profit_amounts()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var before = await WithUnitOfWorkAsync(() => _requestService.GetAsync(order.PurchaseRequestId));
        var beforeOrderAmount = before.PurchaseOrder!.Amount;
        var beforeRequestAmount = before.Amount;
        var beforeSaleAmount = before.SaleAmount;
        var beforeProfit = before.ExpectedGrossProfit;

        await ReceiveAsync(order.Id, "amounts", (Line(order, "A"), 4m));

        var after = await WithUnitOfWorkAsync(() => _requestService.GetAsync(order.PurchaseRequestId));
        Assert.Equal(beforeOrderAmount, after.PurchaseOrder!.Amount);
        Assert.Equal(beforeRequestAmount, after.Amount);
        Assert.Equal(beforeSaleAmount, after.SaleAmount);
        Assert.Equal(beforeProfit, after.ExpectedGrossProfit);
    }

    [Fact]
    public async Task Two_parallel_receipts_cannot_over_receive_the_same_order()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var lineId = Line(order, "A");

        var outcomes = await Task.WhenAll(
            TryReceiveAsync(order.Id, "parallel-1", lineId, 6m),
            TryReceiveAsync(order.Id, "parallel-2", lineId, 6m));

        Assert.Equal(1, outcomes.Count(x => x.Succeeded));
        Assert.Equal(1, outcomes.Count(x => !x.Succeeded));
        var state = await GetReceiptStateAsync(order.Id);
        Assert.Single(state.Receipts);
        Assert.Equal(6m, state.Lines.Single().ReceivedQuantity);
    }

    [Fact]
    public async Task Two_parallel_retries_with_the_same_key_create_one_receipt()
    {
        var order = await CreateOrderAsync(("A", 10m));
        var lineId = Line(order, "A");

        var outcomes = await Task.WhenAll(
            TryReceiveAsync(order.Id, "same-parallel-key", lineId, 4m),
            TryReceiveAsync(order.Id, "same-parallel-key", lineId, 4m));

        Assert.Contains(outcomes, x => x.Succeeded);
        Assert.All(outcomes, x => Assert.True(x.Succeeded || x.Code == PurchaseFlowErrors.ReceiptConcurrencyConflict));
        var state = await GetReceiptStateAsync(order.Id);
        Assert.Single(state.Receipts);
        Assert.Equal(4m, state.Lines.Single().ReceivedQuantity);
    }

    private async Task<PurchaseOrderDto> CreateOrderAsync(params (string ItemName, decimal Quantity)[] lines)
    {
        var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"SO-{Guid.NewGuid():N}",
            Description = "分批收货测试订单",
            SaleAmount = 10000m,
            Lines = lines.Select(line => new CreatePurchaseRequestLineDto
            {
                ItemName = line.ItemName,
                Specification = "测试规格",
                Quantity = line.Quantity,
                UnitPrice = 10m
            }).ToList()
        }));

        // P0-1：报价必须逐项覆盖需求明细。这里两家供应商都按 10 / 11 元逐项报价，
        // 报价总额仍等于原测试的 100 / 110（单行）或 150 / 165（双行），
        // 收货测试只关心数量，金额口径保持自洽。
        var requestDetail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
        var quoteId = await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
            new AddSupplierQuoteDto
            {
                SupplierName = "测试供应商",
                DeliveryDays = 5,
                Lines = QuoteLines(requestDetail, 10m)
            }));
        await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
            new AddSupplierQuoteDto
            {
                SupplierName = "备用供应商",
                DeliveryDays = 6,
                Lines = QuoteLines(requestDetail, 11m)
            }));
        await WithUnitOfWorkAsync(() => _requestService.SelectQuoteAsync(requestId,
            new SelectSupplierQuoteDto { QuoteId = quoteId, Reason = "测试选择" }));
        await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
        await WithUnitOfWorkAsync(() => _requestService.ApproveAsync(requestId,
            new ReviewPurchaseRequestDto { Comment = "测试批准" }));

        var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
        return Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
    }

    private async Task<GoodsReceiptDto> ReceiveAsync(
        Guid orderId,
        string key,
        params (Guid LineId, decimal Quantity)[] lines)
    {
        return await WithUnitOfWorkAsync(() => _receiptService.CreateAsync(new CreateGoodsReceiptDto
        {
            PurchaseOrderId = orderId,
            IdempotencyKey = key,
            ReceiptDate = new DateTime(2026, 10, 31),
            Note = "测试收货",
            Lines = lines.Select(line => new GoodsReceiptLineInputDto
            {
                PurchaseOrderLineId = line.LineId,
                Quantity = line.Quantity
            }).ToList()
        }));
    }

    private async Task<(bool Succeeded, string? Code)> TryReceiveAsync(
        Guid orderId,
        string key,
        Guid lineId,
        decimal quantity)
    {
        try
        {
            await ReceiveAsync(orderId, key, (lineId, quantity));
            return (true, null);
        }
        catch (BusinessException exception)
        {
            return (false, exception.Code);
        }
    }

    private async Task<PurchaseOrderReceiptDto> GetReceiptStateAsync(Guid orderId)
    {
        return await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(orderId));
    }

    private static Guid Line(PurchaseOrderDto order, string itemName) =>
        order.Lines.Single(line => line.ItemName == itemName).Id;

    /// <summary>P0-1：把一条统一单价应用到当前需求的每条明细上。</summary>
    private static List<SupplierQuoteLineInputDto> QuoteLines(PurchaseRequestDetailDto detail, decimal unitPrice) =>
        detail.Lines.Select(line => new SupplierQuoteLineInputDto
        {
            RequestLineId = line.Id,
            UnitPrice = unitPrice
        }).ToList();
}
