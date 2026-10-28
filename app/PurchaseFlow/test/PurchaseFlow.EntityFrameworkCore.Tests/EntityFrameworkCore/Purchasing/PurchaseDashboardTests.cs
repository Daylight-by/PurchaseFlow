using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseDashboardTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IPurchaseRequestAppService _service;
    private readonly IRepository<PurchaseRequest, Guid> _requests;
    private readonly IRepository<SupplierQuote, Guid> _quotes;

    public PurchaseDashboardTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _service = GetRequiredService<IPurchaseRequestAppService>();
        _requests = GetRequiredService<IRepository<PurchaseRequest, Guid>>();
        _quotes = GetRequiredService<IRepository<SupplierQuote, Guid>>();
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
                    var first = await AddQuoteAsync(tenantId, request.Id, $"Pending-{index}-A", 600m, 5);
                    await AddQuoteAsync(tenantId, request.Id, $"Pending-{index}-B", 650m, 7);
                    request.SelectQuote(first.Id, "价格更低");
                    request.Submit(2);
                    await _requests.UpdateAsync(request, autoSave: true);
                    pendingRequests.Add(request);
                }

                approved = await CreateRequestAsync(tenantId, "DASH-APPROVED", 1000m);
                var approvedQuote = await AddQuoteAsync(tenantId, approved.Id, "Approved-A", 600m, 4);
                await AddQuoteAsync(tenantId, approved.Id, "Approved-B", 650m, 6);
                approved.SelectQuote(approvedQuote.Id, "价格更低");
                approved.Submit(2);
                approved.Approve();
                await _requests.UpdateAsync(approved, autoSave: true);

                returned = await CreateRequestAsync(tenantId, "DASH-RETURNED", 1000m);
                var returnedQuote = await AddQuoteAsync(tenantId, returned.Id, "Returned-A", 700m, 4);
                await AddQuoteAsync(tenantId, returned.Id, "Returned-B", 750m, 6);
                returned.SelectQuote(returnedQuote.Id, "价格更低");
                returned.Submit(2);
                returned.Return();
                await _requests.UpdateAsync(returned, autoSave: true);
            }

            using (_currentTenant.Change(otherTenantId))
            {
                otherTenantRequest = await CreateRequestAsync(otherTenantId, "DASH-OTHER", 999m);
                var otherQuote = await AddQuoteAsync(otherTenantId, otherTenantRequest.Id, "Other-A", 100m, 1);
                await AddQuoteAsync(otherTenantId, otherTenantRequest.Id, "Other-B", 110m, 2);
                otherTenantRequest.SelectQuote(otherQuote.Id, "价格更低");
                otherTenantRequest.Submit(2);
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
    public async Task Dashboard_excludes_approved_quote_from_another_request()
    {
        var tenantId = Guid.NewGuid();
        PurchaseRequest requestWithInvalidSelection = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                requestWithInvalidSelection = await CreateRequestAsync(tenantId, "DASH-INVALID", 1000m);
                await AddQuoteAsync(tenantId, requestWithInvalidSelection.Id, "Actual-A", 500m, 5);

                var otherRequest = await CreateRequestAsync(tenantId, "DASH-FOREIGN-QUOTE", 1000m);
                var foreignQuote = await AddQuoteAsync(tenantId, otherRequest.Id, "Foreign-A", 100m, 2);

                requestWithInvalidSelection.SelectQuote(foreignQuote.Id, "跨需求错误选择");
                requestWithInvalidSelection.Submit(2);
                requestWithInvalidSelection.Approve();
                await _requests.UpdateAsync(requestWithInvalidSelection, autoSave: true);
            }
        });

        var dashboard = await WithTenantAsync(tenantId, () => _service.GetDashboardAsync());

        Assert.Equal(2, dashboard.TotalCount);
        Assert.Equal(1, dashboard.ApprovedCount);
        Assert.Equal(2, dashboard.TotalQuoteCount);
        Assert.Equal(0m, dashboard.ApprovedPurchaseAmount);
        Assert.Equal(0m, dashboard.ApprovedExpectedGrossProfit);
    }

    private async Task<PurchaseRequest> CreateRequestAsync(Guid tenantId, string orderReference, decimal saleAmount)
    {
        var request = new PurchaseRequest(Guid.NewGuid(), tenantId, Guid.NewGuid(), orderReference, "仪表盘集成测试", saleAmount);
        await _requests.InsertAsync(request, autoSave: true);
        await Task.Delay(15);
        return request;
    }

    private async Task<SupplierQuote> AddQuoteAsync(
        Guid tenantId,
        Guid requestId,
        string supplierName,
        decimal amount,
        int deliveryDays)
    {
        var quote = new SupplierQuote(Guid.NewGuid(), tenantId, requestId, supplierName, amount, deliveryDays, null);
        await _quotes.InsertAsync(quote, autoSave: true);
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

