using System;
using System.Collections.Generic;
using System.Text;
using PurchaseFlow.Localization;
using Volo.Abp.Application.Services;

namespace PurchaseFlow;

/* Inherit your application services from this class.
 */
public abstract class PurchaseFlowAppService : ApplicationService
{
    protected PurchaseFlowAppService()
    {
        LocalizationResource = typeof(PurchaseFlowResource);
    }
}

