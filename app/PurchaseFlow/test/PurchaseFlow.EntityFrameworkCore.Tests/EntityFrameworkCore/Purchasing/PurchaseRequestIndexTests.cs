using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseRequestIndexTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IRepository<PurchaseRequest, Guid> _requests;

    public PurchaseRequestIndexTests()
    {
        _requests = GetRequiredService<IRepository<PurchaseRequest, Guid>>();
    }

    [Fact]
    public async Task Host_order_reference_must_be_unique_when_tenant_id_is_null()
    {
        var orderReference = $"INDEX-{Guid.NewGuid():N}";
        await WithUnitOfWorkAsync(async () =>
        {
            await _requests.InsertAsync(new PurchaseRequest(Guid.NewGuid(), null, Guid.NewGuid(),
                orderReference, "first request", 100m), autoSave: true);
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _requests.InsertAsync(new PurchaseRequest(Guid.NewGuid(), null, Guid.NewGuid(),
                    orderReference, "duplicate request", 200m), autoSave: true);
            });
        });
    }
}

