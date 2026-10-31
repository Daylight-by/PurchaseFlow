using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// Proves that a receipt can only touch the purchase order and the order lines
/// of the caller: a line of another order is rejected, a receipt of another
/// tenant is rejected, and the persisted state stays untouched.
/// </summary>
[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class GoodsReceiptIsolationTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _requestService;
    private readonly IGoodsReceiptAppService _receiptService;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLineRepository;
    private readonly IRepository<PurchaseOrder, Guid> _orderRepository;

    public GoodsReceiptIsolationTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _orderLineRepository = GetRequiredService<IRepository<PurchaseOrderLine, Guid>>();
        _orderRepository = GetRequiredService<IRepository<PurchaseOrder, Guid>>();
    }

    [Fact]
    public async Task A_first_batch_can_leave_the_second_line_untouched()
    {
        var order = await CreateOrderAsync(("A", 10m), ("B", 5m));

        await ReceiveAsync(order.Id, "first-batch", (Line(order, "A"), 4m));

        var state = await GetReceiptStateAsync(order.Id);
        Assert.True(state.IsPartiallyReceived);
        Assert.False(state.IsFullyReceived);
        Assert.Equal(PurchaseOrderStatus.Created, state.OrderStatus);
        Assert.Equal(4m, state.Lines.Single(x => x.ItemName == "A").ReceivedQuantity);
        Assert.Equal(6m, state.Lines.Single(x => x.ItemName == "A").RemainingQuantity);
        Assert.Equal(0m, state.Lines.Single(x => x.ItemName == "B").ReceivedQuantity);
        Assert.Equal(5m, state.Lines.Single(x => x.ItemName == "B").RemainingQuantity);
        Assert.Single(state.Receipts);
    }

    [Fact]
    public async Task A_line_of_another_order_is_rejected_and_does_not_change_the_other_order()
    {
        var first = await CreateOrderAsync(("A", 10m));
        var second = await CreateOrderAsync(("A", 10m));
        var foreignLineId = Line(second, "A");

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            ReceiveAsync(first.Id, "foreign-line", (foreignLineId, 4m)));

        Assert.Equal(PurchaseFlowErrors.ReceiptLineNotInOrder, exception.Code);

        // The order of the foreign line is not touched at all.
        var foreignLines = await WithUnitOfWorkAsync(() =>
            _orderLineRepository.GetListAsync(x => x.PurchaseOrderId == second.Id));
        Assert.NotEmpty(foreignLines);
        Assert.All(foreignLines, line => Assert.Equal(0m, line.ReceivedQuantity));

        var state = await GetReceiptStateAsync(first.Id);
        Assert.Empty(state.Receipts);
        Assert.All(state.Lines, line => Assert.Equal(0m, line.ReceivedQuantity));
        Assert.Equal(0, await CountReceiptRowsAsync(first.Id));
    }

    [Fact]
    public async Task A_receipt_of_another_tenant_is_rejected_and_writes_nothing()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var order = await CreateOrderAsync(tenantA, ("A", 10m));
        var lineId = Line(order, "A");

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantB))
            {
                await Assert.ThrowsAsync<EntityNotFoundException>(() =>
                    ReceiveAsync(order.Id, "other-tenant", (lineId, 4m)));
            }
        });

        Assert.Equal(0, await CountReceiptRowsAsync(order.Id));
        var lines = await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantA))
                return await _orderLineRepository.GetListAsync(x => x.PurchaseOrderId == order.Id);
        });
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(0m, line.ReceivedQuantity));
    }

    [Fact]
    public async Task A_receipt_stores_the_tenant_and_the_operator_of_the_session()
    {
        var tenantId = Guid.NewGuid();
        var order = await CreateOrderAsync(tenantId, ("A", 10m));

        Guid? expectedCreatorId = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                expectedCreatorId = GetRequiredService<ICurrentUser>().Id;
                await ReceiveAsync(order.Id, "tenant-session", (Line(order, "A"), 4m));
            }
        });

        var stored = await ReadReceiptRowAsync();
        Assert.NotNull(stored);
        Assert.Equal(tenantId, stored!.Value.TenantId);
        Assert.Equal(expectedCreatorId, stored.Value.CreatorId);
    }

    [Fact]
    public async Task A_new_unit_of_work_reads_the_same_progress_and_audit_values()
    {
        var order = await CreateOrderAsync(("A", 10m), ("B", 5m));
        var receipt = await ReceiveAsync(order.Id, "reload", (Line(order, "A"), 4m), (Line(order, "B"), 2m));

        // A fresh unit of work (a new scope and a new database context) reads
        // the persisted values, not the values of the in memory entities.
        var reloaded = await GetReceiptStateAsync(order.Id);
        Assert.Single(reloaded.Receipts);
        Assert.Equal(receipt.Id, reloaded.Receipts.Single().Id);
        Assert.Equal(receipt.CreationTime, reloaded.Receipts.Single().CreationTime);
        Assert.Equal(receipt.CreatorId, reloaded.Receipts.Single().CreatorId);
        Assert.Equal(4m, reloaded.Lines.Single(x => x.ItemName == "A").ReceivedQuantity);
        Assert.Equal(2m, reloaded.Lines.Single(x => x.ItemName == "B").ReceivedQuantity);
        Assert.Equal(6m, reloaded.Lines.Single(x => x.ItemName == "A").RemainingQuantity);
        Assert.Equal(3m, reloaded.Lines.Single(x => x.ItemName == "B").RemainingQuantity);
        Assert.Equal(PurchaseOrderStatus.Created, reloaded.OrderStatus);
    }

    [Fact]
    public async Task The_legacy_return_resubmit_approve_flow_creates_one_order_and_keeps_history()
    {
        var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"R19-{Guid.NewGuid():N}",
            Description = "旧流程回归",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "流程测试物料", Specification = "规格", Quantity = 2m, UnitPrice = 100m }
            }
        }));
        // P0-1：报价逐项录入，180 = 2 * 90，190 = 2 * 95，保持原测试的报价总额。
        var flowDetail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
        var selectedQuoteId = await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
            new AddSupplierQuoteDto
            {
                SupplierName = "流程供应商A",
                DeliveryDays = 4,
                Lines = QuoteLines(flowDetail, 90m)
            }));
        await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
            new AddSupplierQuoteDto
            {
                SupplierName = "流程供应商B",
                DeliveryDays = 5,
                Lines = QuoteLines(flowDetail, 95m)
            }));
        await WithUnitOfWorkAsync(() => _requestService.SelectQuoteAsync(requestId,
            new SelectSupplierQuoteDto { QuoteId = selectedQuoteId, Reason = "选择报价" }));
        await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
        await WithUnitOfWorkAsync(() => _requestService.ReturnAsync(requestId,
            new ReviewPurchaseRequestDto { Comment = "请补充说明" }));
        await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
        await WithUnitOfWorkAsync(() => _requestService.ApproveAsync(requestId,
            new ReviewPurchaseRequestDto { Comment = "复核通过" }));

        var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
        Assert.Equal(PurchaseRequestStatus.Approved, detail.Status);
        Assert.NotNull(detail.PurchaseOrder);
        Assert.Contains(detail.History, entry => entry.Action == "Returned");
        Assert.Contains(detail.History, entry => entry.Action == "Submitted");
        Assert.Contains(detail.History, entry => entry.Action == "Approved");
        var ordersForRequest = await WithUnitOfWorkAsync(() =>
            _orderRepository.GetListAsync(order => order.PurchaseRequestId == requestId));
        Assert.Single(ordersForRequest);
    }

    private Task<PurchaseOrderDto> CreateOrderAsync(params (string ItemName, decimal Quantity)[] lines) =>
        CreateOrderAsync(null, lines);

    private async Task<PurchaseOrderDto> CreateOrderAsync(
        Guid? tenantId,
        params (string ItemName, decimal Quantity)[] lines)
    {
        PurchaseOrderDto? order = null;
        await WithUnitOfWorkAsync(async () =>
        {
            IDisposable? tenantScope = tenantId.HasValue ? _currentTenant.Change(tenantId.Value) : null;
            try
            {
                var requestId = await _requestService.CreateAsync(new CreatePurchaseRequestDto
                {
                    OrderReference = $"SO-{Guid.NewGuid():N}",
                    Description = "隔离测试订单",
                    SaleAmount = 10000m,
                    Lines = lines.Select(line => new CreatePurchaseRequestLineDto
                    {
                        ItemName = line.ItemName,
                        Specification = "测试规格",
                        Quantity = line.Quantity,
                        UnitPrice = 10m
                    }).ToList()
                });

                // P0-1：报价逐项录入，报价总额仍为 100 / 110。
                var requestDetail = await _requestService.GetAsync(requestId);
                var quoteId = await _requestService.AddQuoteAsync(requestId,
                    new AddSupplierQuoteDto
                    {
                        SupplierName = "测试供应商",
                        DeliveryDays = 5,
                        Lines = QuoteLines(requestDetail, 10m)
                    });
                await _requestService.AddQuoteAsync(requestId,
                    new AddSupplierQuoteDto
                    {
                        SupplierName = "备用供应商",
                        DeliveryDays = 6,
                        Lines = QuoteLines(requestDetail, 11m)
                    });
                await _requestService.SelectQuoteAsync(requestId,
                    new SelectSupplierQuoteDto { QuoteId = quoteId, Reason = "测试选择" });
                await _requestService.SubmitAsync(requestId);
                await _requestService.ApproveAsync(requestId,
                    new ReviewPurchaseRequestDto { Comment = "测试批准" });

                var detail = await _requestService.GetAsync(requestId);
                order = Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
            }
            finally
            {
                tenantScope?.Dispose();
            }
        });

        return order!;
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
            Note = "隔离测试收货",
            Lines = lines.Select(line => new GoodsReceiptLineInputDto
            {
                PurchaseOrderLineId = line.LineId,
                Quantity = line.Quantity
            }).ToList()
        }));
    }

    private async Task<PurchaseOrderReceiptDto> GetReceiptStateAsync(Guid orderId)
    {
        return await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(orderId));
    }

    private async Task<int> CountReceiptRowsAsync(Guid orderId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            return await context.GoodsReceipts.IgnoreQueryFilters()
                .CountAsync(receipt => receipt.PurchaseOrderId == orderId);
        });
    }

    private async Task<(Guid? TenantId, Guid? CreatorId)?> ReadReceiptRowAsync()
    {
        (Guid? TenantId, Guid? CreatorId)? result = null;
        await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            var row = await context.GoodsReceipts.IgnoreQueryFilters()
                .Select(x => new { x.TenantId, x.CreatorId })
                .FirstOrDefaultAsync();
            result = row == null ? null : (row.TenantId, row.CreatorId);
        });
        return result;
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
