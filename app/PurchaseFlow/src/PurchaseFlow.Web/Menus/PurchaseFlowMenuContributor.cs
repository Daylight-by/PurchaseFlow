using System.Threading.Tasks;
using PurchaseFlow.Localization;
using PurchaseFlow.MultiTenancy;
using PurchaseFlow.Permissions;
using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.TenantManagement.Web.Navigation;
using Volo.Abp.UI.Navigation;

namespace PurchaseFlow.Web.Menus;

public class PurchaseFlowMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }

    private async Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var administration = context.Menu.GetAdministration();
        var l = context.GetLocalizer<PurchaseFlowResource>();

        context.Menu.Items.Insert(
            0,
            new ApplicationMenuItem(
                PurchaseFlowMenus.Home,
                l["Menu:Home"],
                "~/",
                icon: "fas fa-home",
                order: 0
            )
        );

        if (await context.IsGrantedAsync(PurchaseFlowPermissions.Requests))
        {
            context.Menu.AddItem(new ApplicationMenuItem(
                PurchaseFlowMenus.PurchaseRequests,
                l["Menu:PurchaseRequests"],
                "~/Purchasing/Index",
                icon: "fas fa-file-invoice-dollar",
                order: 1
            ));
        }

        if (MultiTenancyConsts.IsEnabled)
        {
            administration.SetSubItemOrder(TenantManagementMenuNames.GroupName, 1);
        }
        else
        {
            administration.TryRemoveMenuItem(TenantManagementMenuNames.GroupName);
        }

        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 2);
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 3);

    }
}

