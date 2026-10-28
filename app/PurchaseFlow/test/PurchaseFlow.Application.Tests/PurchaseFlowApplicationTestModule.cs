using Volo.Abp.Modularity;

namespace PurchaseFlow;

[DependsOn(
    typeof(PurchaseFlowApplicationModule),
    typeof(PurchaseFlowDomainTestModule)
)]
public class PurchaseFlowApplicationTestModule : AbpModule
{

}

