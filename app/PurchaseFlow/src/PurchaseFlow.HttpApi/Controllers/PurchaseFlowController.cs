using PurchaseFlow.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace PurchaseFlow.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class PurchaseFlowController : AbpControllerBase
{
    protected PurchaseFlowController()
    {
        LocalizationResource = typeof(PurchaseFlowResource);
    }
}

