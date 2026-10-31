using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseOrderTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _service;

    public PurchaseOrderTests()
    {
        _service = GetRequiredService<IPurchaseRequestAppService>();
    }

    [Fact]
    public async Task Approving_a_request_creates_an_itemized_order_from_the_selected_quote()
    {
        var orderReference = $"PO-{Guid.NewGuid():N}";
        // 需求明细：10 * 25.5 + 4.5 * 12 = 309.00。
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = orderReference,
            Description = "审批后生成采购订单",
            SaleAmount = 100000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "深沟球轴承", Specification = "6205-2RS", Quantity = 10m, UnitPrice = 25.5m },
                new() { ItemName = "骨架油封", Specification = "NBR 20x3", Quantity = 4.5m, UnitPrice = 12m }
            }
        }));

        // P0-1：报价改为逐项报价，测试数据必须自洽：
        // 华东机电 10*20 + 4.5*10 = 245.00；南方轴承 10*25 + 4.5*12.5 = 306.25。
        var quoteId = await AddQuoteAsync(id, "华东机电", 10,
            ("深沟球轴承", 20m), ("骨架油封", 10m));
        await AddQuoteAsync(id, "南方轴承", 5,
            ("深沟球轴承", 25m), ("骨架油封", 12.5m));

        await WithUnitOfWorkAsync(async () => await _service.SelectQuoteAsync(id, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = "价格更低"
        }));
        await WithUnitOfWorkAsync(async () => await _service.SubmitAsync(id));
        await WithUnitOfWorkAsync(async () => await _service.ApproveAsync(id, new ReviewPurchaseRequestDto
        {
            Comment = "同意"
        }));

        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        // 报价自身自洽：报价总额 == 报价明细合计。
        var selectedQuote = detail.Quotes.Single(x => x.Id == quoteId);
        Assert.Equal(245m, selectedQuote.Amount);
        Assert.Equal(245m, selectedQuote.Lines.Sum(x => x.Subtotal));

        var order = Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
        Assert.Equal($"PO-{orderReference}", order.OrderNumber);
        Assert.Equal("华东机电", order.SupplierName);
        Assert.Equal(10, order.DeliveryDays);
        Assert.Equal(PurchaseOrderStatus.Created, order.Status);
        Assert.Equal(2, order.Lines.Count);

        // 订单金额 == 订单明细合计 == 报价明细合计，且订单明细单价来自报价（成交单价）。
        Assert.Equal(245m, order.Amount);
        Assert.Equal(245m, order.Lines.Sum(x => x.Subtotal));
        Assert.Equal(selectedQuote.Amount, order.Amount);
        Assert.Equal(
            new[] { 20m, 10m }.OrderBy(x => x),
            order.Lines.Select(x => x.UnitPrice).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "深沟球轴承", "骨架油封" }.OrderBy(x => x),
            order.Lines.Select(x => x.ItemName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task The_longest_allowed_order_reference_still_produces_a_stored_order_number()
    {
        // P1-1：OrderReference 允许 64 个字符，拼接 "PO-" 后为 67 个字符。
        // 订单号上限已扩到 128，这里验证边界值能生成、落库、读回。
        var orderReference = new string('R', 64);
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = orderReference,
            Description = "订单号边界长度",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "电机", Specification = "YS-90", Quantity = 2m, UnitPrice = 350m }
            }
        }));

        var quoteId = await AddQuoteAsync(id, "边界供应商", 3, ("电机", 350m));
        await AddQuoteAsync(id, "备用供应商", 5, ("电机", 360m));
        await WithUnitOfWorkAsync(async () => await _service.SelectQuoteAsync(id, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = "价格更低"
        }));
        await WithUnitOfWorkAsync(async () => await _service.SubmitAsync(id));
        await WithUnitOfWorkAsync(async () => await _service.ApproveAsync(id, new ReviewPurchaseRequestDto
        {
            Comment = "同意"
        }));

        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        var order = Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
        Assert.Equal($"PO-{orderReference}", order.OrderNumber);
        Assert.Equal(67, order.OrderNumber.Length);
        Assert.Equal(700m, order.Amount);
        Assert.Equal(700m, order.Lines.Sum(x => x.Subtotal));
    }

    [Fact]
    public async Task A_draft_request_has_no_purchase_order()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"PO-{Guid.NewGuid():N}",
            Description = "草稿还没有订单",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "电机", Specification = "YS-90", Quantity = 2m, UnitPrice = 350m }
            }
        }));

        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        Assert.Null(detail.PurchaseOrder);
    }

    /// <summary>
    /// 按当前需求明细逐项报价：物料名到报价单价的映射必须与需求明细一一对应，
    /// 否则“报价总额 = 明细合计”的断言就会依赖排序这种偶然因素。
    /// </summary>
    private async Task<Guid> AddQuoteAsync(Guid requestId, string supplierName, int deliveryDays,
        params (string ItemName, decimal UnitPrice)[] prices)
    {
        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(requestId));
        var lines = detail.Lines.Select(line =>
        {
            var price = prices.Single(x => x.ItemName == line.ItemName);
            return new SupplierQuoteLineInputDto
            {
                RequestLineId = line.Id,
                UnitPrice = price.UnitPrice
            };
        }).ToList();

        return await WithUnitOfWorkAsync(async () => await _service.AddQuoteAsync(requestId,
            new AddSupplierQuoteDto
            {
                SupplierName = supplierName,
                DeliveryDays = deliveryDays,
                Lines = lines
            }));
    }
}
