using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// R13: a failure in the middle of the receipt write must roll back the
/// receipt head, the receipt lines, the received quantity of the order lines
/// and the audit data. The failure is injected by the database itself: a
/// trigger aborts the insert of the receipt line, so the head row is already
/// written when the write fails.
/// </summary>
[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class GoodsReceiptRollbackTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _requestService;
    private readonly IGoodsReceiptAppService _receiptService;
    private readonly IRepository<GoodsReceipt, Guid> _receiptRepository;
    private readonly IRepository<GoodsReceiptLine, Guid> _receiptLineRepository;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLineRepository;

    public GoodsReceiptRollbackTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
        _receiptRepository = GetRequiredService<IRepository<GoodsReceipt, Guid>>();
        _receiptLineRepository = GetRequiredService<IRepository<GoodsReceiptLine, Guid>>();
        _orderLineRepository = GetRequiredService<IRepository<PurchaseOrderLine, Guid>>();
    }

    [Fact]
    public async Task A_failure_in_the_middle_of_the_write_rolls_back_everything()
    {
        var order = await CreateOrderAsync(("A", 10m), ("B", 5m));
        var lineA = Line(order, "A");
        var lineB = Line(order, "B");

        await InstallFailingTriggerAsync();

        try
        {
            var exception = await Assert.ThrowsAsync<BusinessException>(() =>
                ReceiveAsync(order.Id, "rollback", (lineA, 4m), (lineB, 5m)));

            // The database error is reported as a business error, the caller
            // never sees a raw persistence exception.
            Assert.Equal(PurchaseFlowErrors.ReceiptConcurrencyConflict, exception.Code);

            // Nothing of the receipt is stored.
            var receiptsForOrder = await WithUnitOfWorkAsync(() =>
                _receiptRepository.GetListAsync(x => x.PurchaseOrderId == order.Id));
            Assert.Empty(receiptsForOrder);
            var linesForReceipts = await WithUnitOfWorkAsync(async () =>
            {
                var context = GetRequiredService<PurchaseFlowDbContext>();
                return await context.GoodsReceiptLines
                    .Where(x => receiptsForOrder.Select(receipt => receipt.Id).Contains(x.GoodsReceiptId))
                    .ToListAsync();
            });
            Assert.Empty(linesForReceipts);

            // The progress of the order lines is unchanged.
            var lines = await WithUnitOfWorkAsync(() =>
                _orderLineRepository.GetListAsync(x => x.PurchaseOrderId == order.Id));
            Assert.Equal(2, lines.Count);
            Assert.All(lines, line => Assert.Equal(0m, line.ReceivedQuantity));

            // The order itself is unchanged and still open for a receipt.
            var state = await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(order.Id));
            Assert.Empty(state.Receipts);
            Assert.Equal(PurchaseOrderStatus.Created, state.OrderStatus);
            Assert.Equal(10m, state.Lines.Single(x => x.ItemName == "A").RemainingQuantity);
        }
        finally
        {
            await RemoveFailingTriggerAsync();
        }

        // After the failure the order can be received normally.
        var receipt = await ReceiveAsync(order.Id, "after-rollback", (lineA, 4m));
        Assert.True(receipt.Id != Guid.Empty);
        var after = await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(order.Id));
        Assert.Single(after.Receipts);
        Assert.Equal(4m, after.Lines.Single(x => x.ItemName == "A").ReceivedQuantity);
    }

    /// <summary>
    /// The trigger aborts every receipt line with a quantity of 13, so the
    /// write fails after the receipt head has been written.
    /// </summary>
    private async Task InstallFailingTriggerAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                @"CREATE TRIGGER IF NOT EXISTS TR_FailReceiptLine
                  BEFORE INSERT ON AppGoodsReceiptLines
                  WHEN 1 = 1
                  BEGIN
                    SELECT RAISE(ABORT, 'injected failure');
                  END;");
        });
    }

    private async Task RemoveFailingTriggerAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_FailReceiptLine;");
        });
    }

    private async Task<PurchaseOrderDto> CreateOrderAsync(params (string ItemName, decimal Quantity)[] lines)
    {
        var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"SO-{Guid.NewGuid():N}",
            Description = "回滚测试订单",
            SaleAmount = 10000m,
            Lines = lines.Select(line => new CreatePurchaseRequestLineDto
            {
                ItemName = line.ItemName,
                Specification = "测试规格",
                Quantity = line.Quantity,
                UnitPrice = 10m
            }).ToList()
        }));

        // P0-1：报价改为逐项报价，测试数据保持“报价总额 = 明细合计”。
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
            Note = "回滚测试收货",
            Lines = lines.Select(line => new GoodsReceiptLineInputDto
            {
                PurchaseOrderLineId = line.LineId,
                Quantity = line.Quantity
            }).ToList()
        }));
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
