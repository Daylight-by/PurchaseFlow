using PurchaseFlow.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace PurchaseFlow.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(PurchaseFlowEntityFrameworkCoreModule),
    typeof(PurchaseFlowApplicationContractsModule)
    )]
public class PurchaseFlowDbMigratorModule : AbpModule
{
}

