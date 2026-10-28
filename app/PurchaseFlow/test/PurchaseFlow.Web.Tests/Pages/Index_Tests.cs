using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace PurchaseFlow.Pages;

public class Index_Tests : PurchaseFlowWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}

