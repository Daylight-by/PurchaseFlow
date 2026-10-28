using PurchaseFlow.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace PurchaseFlow.Web.Pages;

/* Inherit your PageModel classes from this class.
 */
public abstract class PurchaseFlowPageModel : AbpPageModel
{
    protected PurchaseFlowPageModel()
    {
        LocalizationResourceType = typeof(PurchaseFlowResource);
    }
}

