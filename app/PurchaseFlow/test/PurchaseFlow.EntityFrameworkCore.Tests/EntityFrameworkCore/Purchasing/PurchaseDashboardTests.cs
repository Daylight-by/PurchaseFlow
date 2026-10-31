using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseDashboardTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IPurchaseRequestAppService _service;
    private readonly IPurchaseRequestRepository _requests;

    public PurchaseDashboardTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _service = GetRequiredService<IPurchaseRequestAppService>();
        _requests = GetRequiredService<IPurchaseRequestRepository>();
    }

    [Fact]
    public async Task Dashboard_counts_current_tenant_and_orders_request_lists()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var pendingRequests = new List<PurchaseRequest>();
        PurchaseRequest draft = null!;
        PurchaseRequest approved = null!;
        PurchaseRequest returned = null!;
        PurchaseRequest otherTenantRequest = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                draft = await CreateRequestAsync(tenantId, "DASH-DRAFT", 1000m);

                for (var index = 1; index <= 6; index++)
                {
                    var request = await CreateRequestAsync(tenantId, $"DASH-PENDING-{index}", 1000m + index);
                    var first = await AddQuoteAsync(request, $"Pending-{index}-A", 600m, 5);
                    await AddQuoteAsync(request, $"Pending-{index}-B", 650m, 7);
                    request.SelectQuote(first.Id, "价格更低");
                    request.Submit();
                    await _requests.UpdateAsync(request, autoSave: true);
                    pendingRequests.Add(request);
                }

                approved = await CreateRequestAsync(tenantId, "DASH-APPROVED", 1000m);
                var approvedQuote = await AddQuoteAsync(approved, "Approved-A", 600m, 4);
                await AddQuoteAsync(approved, "Approved-B", 650m, 6);
                approved.SelectQuote(approvedQuote.Id, "价格更低");
                approved.Submit();
                approved.Approve();
                await _requests.UpdateAsync(approved, autoSave: true);

                returned = await CreateRequestAsync(tenantId, "DASH-RETURNED", 1000m);
                var returnedQuote = await AddQuoteAsync(returned, "Returned-A", 700m, 4);
                await AddQuoteAsync(returned, "Returned-B", 750m, 6);
                returned.SelectQuote(returnedQuote.Id, "价格更低");
                returned.Submit();
                returned.Return();
                await _requests.UpdateAsync(returned, autoSave: true);
            }

            using (_currentTenant.Change(otherTenantId))
            {
                otherTenantRequest = await CreateRequestAsync(otherTenantId, "DASH-OTHER", 999m);
                var otherQuote = await AddQuoteAsync(otherTenantRequest, "Other-A", 100m, 1);
                await AddQuoteAsync(otherTenantRequest, "Other-B", 110m, 2);
                otherTenantRequest.SelectQuote(otherQuote.Id, "价格更低");
                otherTenantRequest.Submit();
                otherTenantRequest.Approve();
                await _requests.UpdateAsync(otherTenantRequest, autoSave: true);
            }
        });

        var dashboard = await WithTenantAsync(tenantId, () => _service.GetDashboardAsync());

        Assert.Equal(9, dashboard.TotalCount);
        Assert.Equal(1, dashboard.DraftCount);
        Assert.Equal(6, dashboard.PendingReviewCount);
        Assert.Equal(1, dashboard.ReturnedCount);
        Assert.Equal(1, dashboard.ApprovedCount);
        Assert.Equal(16, dashboard.TotalQuoteCount);
        Assert.Equal(600m, dashboard.ApprovedPurchaseAmount);
        Assert.Equal(400m, dashboard.ApprovedExpectedGrossProfit);

        Assert.Equal(
            pendingRequests.OrderBy(x => x.CreationTime).ThenBy(x => x.Id).Take(5).Select(x => x.Id),
            dashboard.PendingReviews.Select(x => x.Id));
        Assert.Equal(
            pendingRequests.Concat(new[] { draft, approved, returned })
                .OrderByDescending(x => x.CreationTime)
                .ThenByDescending(x => x.Id)
                .Take(5)
                .Select(x => x.Id),
            dashboard.RecentRequests.Select(x => x.Id));
        Assert.DoesNotContain(otherTenantRequest.Id, dashboard.RecentRequests.Select(x => x.Id));
        Assert.DoesNotContain(otherTenantRequest.Id, dashboard.PendingReviews.Select(x => x.Id));
    }

    [Fact]
    public async Task Dashboard_only_counts_a_quote_that_belongs_to_the_approved_request()
    {
        var tenantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var request = await CreateRequestAsync(tenantId, "DASH-VALID", 1000m);
                var quote = await AddQuoteAsync(request, "Valid-A", 500m, 5);
                await AddQuoteAsync(request, "Valid-B", 560m, 7);
                request.SelectQuote(quote.Id, "价格更低");
                request.Submit();
                request.Approve();
                await _requests.UpdateAsync(request, autoSave: true);
            }
        });

        var dashboard = await WithTenantAsync(tenantId, () => _service.GetDashboardAsync());

        Assert.Equal(1, dashboard.ApprovedCount);
        Assert.Equal(500m, dashboard.ApprovedPurchaseAmount);
        Assert.Equal(500m, dashboard.ApprovedExpectedGrossProfit);
    }

    [Fact]
    public async Task A_quote_of_another_request_can_not_be_selected()
    {
        var tenantId = Guid.NewGuid();
        Guid foreignQuoteId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var request = await CreateRequestAsync(tenantId, "DASH-INVALID", 1000m);
                var otherRequest = await CreateRequestAsync(tenantId, "DASH-OTHER-REQUEST", 1000m);
                var foreignQuote = await AddQuoteAsync(otherRequest, "Foreign-A", 100m, 2);

                Assert.Throws<EntityNotFoundException>(() => request.SelectQuote(foreignQuote.Id, "跨需求错误选择"));
                await Task.CompletedTask;
            }
        });
    }

    private async Task<PurchaseRequest> CreateRequestAsync(Guid tenantId, string orderReference, decimal saleAmount)
    {
        var request = new PurchaseRequest(Guid.NewGuid(), tenantId, Guid.NewGuid(), orderReference, "仪表盘集成测试", saleAmount);
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 100m);
        await _requests.InsertAsync(request, autoSave: true);
        await Task.Delay(15);
        return request;
    }

    private async Task<SupplierQuote> AddQuoteAsync(
        PurchaseRequest request,
        string supplierName,
        decimal amount,
        int deliveryDays)
    {
        // P0-1：报价改为逐项报价，这里把目标总额平均分摊到需求明细上，
        // 保证“报价总额 = 报价明细合计”，同时保持各测试预期的报价金额不变。
        var quotedLines = request.Lines
            .Select(line => (Id: Guid.NewGuid(), RequestLineId: line.Id,
                UnitPrice: amount / line.Quantity))
            .ToList();
        var quote = request.AddQuote(Guid.NewGuid(), supplierName, deliveryDays, null, quotedLines);
        await _requests.UpdateAsync(request, autoSave: true);
        return quote;
    }

    private Task<PurchaseDashboardDto> WithTenantAsync(Guid tenantId, Func<Task<PurchaseDashboardDto>> action)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await action();
            }
        });
    }
}
