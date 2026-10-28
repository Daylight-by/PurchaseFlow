using Microsoft.Extensions.Localization;
using PurchaseFlow.Localization;
using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;

namespace PurchaseFlow.Web;

[Dependency(ReplaceServices = true)]
public class PurchaseFlowBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<PurchaseFlowResource> _localizer;

    public PurchaseFlowBrandingProvider(IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}

