using Volo.Abp.Modularity;

namespace PurchaseFlow;

[DependsOn(
    typeof(PurchaseFlowDomainModule),
    typeof(PurchaseFlowTestBaseModule)
)]
public class PurchaseFlowDomainTestModule : AbpModule
{

}

