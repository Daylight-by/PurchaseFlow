using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseFlow.Data;
using Volo.Abp.DependencyInjection;

namespace PurchaseFlow.EntityFrameworkCore;

public class EntityFrameworkCorePurchaseFlowDbSchemaMigrator
    : IPurchaseFlowDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCorePurchaseFlowDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve the PurchaseFlowDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<PurchaseFlowDbContext>()
            .Database
            .MigrateAsync();
    }
}

