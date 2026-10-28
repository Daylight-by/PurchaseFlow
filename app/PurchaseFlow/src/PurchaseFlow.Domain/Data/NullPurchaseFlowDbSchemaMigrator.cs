using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace PurchaseFlow.Data;

/* This is used if database provider does't define
 * IPurchaseFlowDbSchemaMigrator implementation.
 */
public class NullPurchaseFlowDbSchemaMigrator : IPurchaseFlowDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}

