using Volo.Abp.Modularity;

namespace PurchaseFlow;

public abstract class PurchaseFlowApplicationTestBase<TStartupModule> : PurchaseFlowTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}

