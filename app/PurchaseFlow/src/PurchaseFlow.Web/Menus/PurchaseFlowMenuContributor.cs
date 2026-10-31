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

        // 第二轮第 1 批：采购需求之后放「供应商档案」「物料档案」两项（order 2/3）。
        // 用 MasterData 权限控制菜单可见性：能看档案的角色都能在录入页选择档案，
        // 但只有 ManageMasterData 才在档案页面看到写操作。
        if (await context.IsGrantedAsync(PurchaseFlowPermissions.MasterData))
        {
            context.Menu.AddItem(new ApplicationMenuItem(
                PurchaseFlowMenus.Suppliers,
                "供应商档案",
                "~/MasterData/Suppliers",
                icon: "fas fa-truck",
                order: 2
            ));
            context.Menu.AddItem(new ApplicationMenuItem(
                PurchaseFlowMenus.Materials,
                "物料档案",
                "~/MasterData/Materials",
                icon: "fas fa-boxes-stacked",
                order: 3
            ));
        }

        // 第三轮：发票与付款总览（只读）。查看权限 ViewInvoices 控制菜单可见性，
        // 登记/审批按钮在订单详情页内按 RegisterInvoices / ApprovePayments 单独控制。
        if (await context.IsGrantedAsync(PurchaseFlowPermissions.ViewInvoices))
        {
            context.Menu.AddItem(new ApplicationMenuItem(
                PurchaseFlowMenus.Invoices,
                "发票与付款",
                "~/Finance/Invoices",
                icon: "fas fa-file-invoice",
                order: 4
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

