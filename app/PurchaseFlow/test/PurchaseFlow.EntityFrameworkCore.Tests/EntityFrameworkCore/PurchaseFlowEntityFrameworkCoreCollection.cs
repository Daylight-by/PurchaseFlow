using Xunit;

namespace PurchaseFlow.EntityFrameworkCore;

[CollectionDefinition(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseFlowEntityFrameworkCoreCollection : ICollectionFixture<PurchaseFlowEntityFrameworkCoreFixture>
{

}

