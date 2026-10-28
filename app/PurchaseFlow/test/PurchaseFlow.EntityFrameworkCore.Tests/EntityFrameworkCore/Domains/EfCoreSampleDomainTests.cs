using PurchaseFlow.Samples;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Domains;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<PurchaseFlowEntityFrameworkCoreTestModule>
{

}

