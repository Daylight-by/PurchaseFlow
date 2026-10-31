using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace PurchaseFlow.EntityFrameworkCore;

/// <summary>
/// The purchase request owns two collections (its lines and its quotes). The
/// default repository only loads reference navigations, so this repository
/// loads the collections explicitly and hands the aggregate root over as a
/// complete object graph.
/// </summary>
public class PurchaseRequestEfCoreRepository
    : EfCoreRepository<PurchaseFlowDbContext, PurchaseRequest, Guid>, IPurchaseRequestRepository
{
    public PurchaseRequestEfCoreRepository(IDbContextProvider<PurchaseFlowDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<PurchaseRequest> FindWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await FindAsync(id, includeDetails: false, cancellationToken: cancellationToken);
        if (request is null)
            return null!;

        await EnsureCollectionLoadedAsync(request, x => x.Lines, cancellationToken);
        await EnsureCollectionLoadedAsync(request, x => x.Quotes, cancellationToken);

        // P0-1：报价现在带逐项明细，审批生成采购订单时必须能拿到报价（成交）单价。
        // EnsureCollectionLoadedAsync 只能加载聚合根本身的集合，所以这里再对每条
        // 报价显式加载 Lines，保证 FindWithDetailsAsync 返回完整对象图。
        var dbContext = await GetDbContextAsync();
        foreach (var quote in request.Quotes)
            await dbContext.Entry(quote).Collection(x => x.Lines).LoadAsync(cancellationToken);

        return request;
    }
}
