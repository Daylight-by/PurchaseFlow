using PurchaseFlow.Samples;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Applications;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<PurchaseFlowEntityFrameworkCoreTestModule>
{

}

