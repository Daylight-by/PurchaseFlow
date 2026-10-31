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
/// 第二轮第 3 批（报价有效期 + 历史采购价）的集成测试：
/// 已过期报价在应用入口被拒；有效报价可走完整流程且订单明细继承物料档案；
/// 历史采购价返回该物料最近一单的成交价；详情 DTO 能标记过期报价。
/// </summary>
[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class QuoteValidityAndPriceHistoryTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _requests;
    private readonly IMaterialAppService _materials;
    private readonly IPurchaseRequestRepository _requestRepository;

    public QuoteValidityAndPriceHistoryTests()
    {
        _requests = GetRequiredService<IPurchaseRequestAppService>();
        _materials = GetRequiredService<IMaterialAppService>();
        _requestRepository = GetRequiredService<IPurchaseRequestRepository>();
    }

    [Fact]
    public async Task Creating_an_already_expired_quote_is_rejected()
    {
        var requestId = await CreateRequestAsync(materialId: null, quantity: 1m, unitPrice: 100m);
        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var line = detail.Lines.Single();

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
            {
                SupplierName = "过期供应商",
                DeliveryDays = 5,
                ValidUntil = DateTime.UtcNow.Date.AddDays(-1),
                Lines = new List<SupplierQuoteLineInputDto>
                {
                    new() { RequestLineId = line.Id, UnitPrice = 90m }
                }
            })));
        Assert.Equal(PurchaseFlowErrors.QuoteExpired, exception.Code);
    }

    [Fact]
    public async Task A_valid_quote_flows_to_an_order_and_the_order_line_keeps_the_material()
    {
        var material = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-VALID-01",
            Name = "有效期测试物料",
            Specification = "规格",
            Unit = "个"
        }));

        var (requestId, orderId, orderNumber, amount, _) = await PlaceOrderAsync(
            material.Id, quantity: 2m, budgetUnitPrice: 100m, quoteUnitPrice: 90m);

        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var order = Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
        Assert.Equal(orderId, order.Id);
        Assert.Equal(orderNumber, order.OrderNumber);
        Assert.Equal(180m, amount);
        Assert.Equal(amount, order.Lines.Sum(x => x.Subtotal));
        Assert.All(order.Lines, line => Assert.Equal(material.Id, line.MaterialId));
    }

    [Fact]
    public async Task Last_purchase_price_returns_the_latest_order_for_the_material()
    {
        var material = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-HIST-01",
            Name = "历史价测试物料",
            Specification = "规格",
            Unit = "个"
        }));

        var first = await PlaceOrderAsync(material.Id, quantity: 1m, budgetUnitPrice: 100m, quoteUnitPrice: 90m);
        // 订单 CreationTime 精确度有限，稍等再下第二单，确保“最近一单”可判定。
        await Task.Delay(30);
        var second = await PlaceOrderAsync(material.Id, quantity: 1m, budgetUnitPrice: 150m, quoteUnitPrice: 120m);
        var reference = await WithUnitOfWorkAsync(() => _materials.GetLastPurchasePriceAsync(material.Id));

        Assert.NotNull(reference);
        Assert.Equal(material.Id, reference!.MaterialId);
        Assert.Equal(120m, reference.UnitPrice);
        Assert.Equal(second.OrderNumber, reference.OrderNumber);
        Assert.True(reference.OrderedAt >= first.OrderedAt);

        // 没有任何采购订单的物料返回 null，页面据此显示“暂无历史采购价”。
        var other = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-HIST-NONE",
            Name = "无历史物料",
            Unit = "个"
        }));
        var noHistory = await WithUnitOfWorkAsync(() => _materials.GetLastPurchasePriceAsync(other.Id));
        Assert.Null(noHistory);
    }

    [Fact]
    public async Task The_detail_dto_marks_an_expired_quote()
    {
        var requestId = await CreateRequestAsync(materialId: null, quantity: 1m, unitPrice: 100m);

        // 应用服务会拒绝新增已过期报价；这里通过领域聚合预置一条历史报价，
        // 用于验证详情 DTO 的 IsExpired/ValidUntil 展示口径。
        await WithUnitOfWorkAsync(async () =>
        {
            var request = await _requestRepository.FindWithDetailsAsync(requestId);
            var line = request!.Lines.Single();
            request.AddQuote(
                Guid.NewGuid(),
                "历史过期供应商",
                5,
                null,
                new List<(Guid Id, Guid RequestLineId, decimal UnitPrice)>
                {
                    (Guid.NewGuid(), line.Id, 88m)
                },
                supplierId: null,
                validUntil: DateTime.UtcNow.Date.AddDays(-2));
            await _requestRepository.UpdateAsync(request, autoSave: true);
        });

        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var expired = detail.Quotes.Single(x => x.SupplierName == "历史过期供应商");
        Assert.True(expired.IsExpired);
        Assert.NotNull(expired.ValidUntil);
        Assert.True(expired.ValidUntil!.Value.Date < DateTime.UtcNow.Date);
    }

    private async Task<Guid> CreateRequestAsync(Guid? materialId, decimal quantity, decimal unitPrice)
    {
        return await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"VALID-{Guid.NewGuid():N}",
            Description = "报价有效期与历史价测试",
            SaleAmount = 10000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new()
                {
                    MaterialId = materialId,
                    ItemName = materialId.HasValue ? "占位名称" : "手工物料",
                    Specification = "规格",
                    Quantity = quantity,
                    UnitPrice = unitPrice
                }
            }
        }));
    }

    /// <summary>创建请求 → 两家有效报价 → 选价 → 提交 → 审批（低金额） → 返回订单信息。</summary>
    private async Task<(Guid RequestId, Guid OrderId, string OrderNumber, decimal Amount, DateTime OrderedAt)> PlaceOrderAsync(
        Guid materialId,
        decimal quantity,
        decimal budgetUnitPrice,
        decimal quoteUnitPrice)
    {
        var requestId = await CreateRequestAsync(materialId, quantity, budgetUnitPrice);
        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var line = detail.Lines.Single();
        var validUntil = DateTime.UtcNow.Date.AddDays(30);

        var quoteId = await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierName = "历史价供应商A",
            DeliveryDays = 5,
            ValidUntil = validUntil,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = line.Id, UnitPrice = quoteUnitPrice }
            }
        }));
        await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierName = "历史价供应商B",
            DeliveryDays = 6,
            ValidUntil = validUntil,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = line.Id, UnitPrice = quoteUnitPrice + 5m }
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
        return (requestId, order.Id, order.OrderNumber, order.Amount, order.CreationTime);
    }
}
