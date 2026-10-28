using Volo.Abp.Modularity;

namespace PurchaseFlow;

/* Inherit from this class for your domain layer tests. */
public abstract class PurchaseFlowDomainTestBase<TStartupModule> : PurchaseFlowTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}

