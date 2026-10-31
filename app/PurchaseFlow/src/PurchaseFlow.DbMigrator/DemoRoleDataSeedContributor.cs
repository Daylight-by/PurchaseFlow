using System;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace PurchaseFlow.DbMigrator;

/// <summary>
/// Creates reusable demo roles and demo users with well-known demo passwords so
/// a freshly deployed instance is directly usable for evaluation. These are
/// sample accounts meant for public demonstration, not production credentials.
/// </summary>
public class DemoRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IGuidGenerator _guidGenerator;

    public DemoRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IdentityUserManager userManager,
        IPermissionManager permissionManager,
        IGuidGenerator guidGenerator)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _permissionManager = permissionManager;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // Demo identities are host-only. Tenant administrators can define their
        // own roles and users without inheriting these sample accounts.
        if (context.TenantId.HasValue) return;

        await EnsureRoleAsync("buyer", PurchaseFlowPermissions.EditRequests, canViewReceipts: true);
        await EnsureRoleAsync("reviewer", PurchaseFlowPermissions.ReviewRequests, canViewReceipts: true);
        await EnsureRoleAsync("receiver", PurchaseFlowPermissions.RegisterReceipts, canViewReceipts: true);

        // 第二轮主数据：管理员（ABP 模板 IdentityDataSeeder 创建）同时拥有查看与维护权限；
        // 业务角色只授予查看（see EnsureRoleAsync），避免演示账号误改档案。
        await _permissionManager.SetForRoleAsync("admin", PurchaseFlowPermissions.MasterData, true);
        await _permissionManager.SetForRoleAsync("admin", PurchaseFlowPermissions.ManageMasterData, true);

        // Demo accounts are created unconditionally with well-known demo
        // passwords, so a freshly deployed instance is directly usable for
        // evaluation (the credentials are shown on the login page).
        await EnsureUserAsync("demo.buyer", "buyer@example.invalid", "buyer", "Buyer@Demo123");
        await EnsureUserAsync("demo.reviewer", "reviewer@example.invalid", "reviewer", "Reviewer@Demo123");
        await EnsureUserAsync("demo.receiver", "receiver@example.invalid", "receiver",
            Environment.GetEnvironmentVariable("Demo__ReceiverPassword"));
    }

    private async Task EnsureRoleAsync(string name, string actionPermission, bool canViewReceipts = false)
    {
        if (await _roleManager.FindByNameAsync(name) is null)
            EnsureSucceeded(await _roleManager.CreateAsync(new IdentityRole(_guidGenerator.Create(), name)));

        await _permissionManager.SetForRoleAsync(name, PurchaseFlowPermissions.Requests, true);
        await _permissionManager.SetForRoleAsync(name, actionPermission, true);
        await _permissionManager.SetForRoleAsync(name, PurchaseFlowPermissions.ViewReceipts, canViewReceipts);
        await _permissionManager.SetForRoleAsync(name, PurchaseFlowPermissions.RegisterReceipts,
            string.Equals(actionPermission, PurchaseFlowPermissions.RegisterReceipts, StringComparison.Ordinal));
        // 第二轮主数据：业务角色都能“查看”供应商/物料档案（用于报价与需求明细选择），
        // 但不授予 ManageMasterData —— 维护权限只给管理员。
        await _permissionManager.SetForRoleAsync(name, PurchaseFlowPermissions.MasterData, true);
    }

    private async Task EnsureUserAsync(string userName, string email, string role, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return;

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new IdentityUser(_guidGenerator.Create(), userName, email);
            EnsureSucceeded(await _userManager.CreateAsync(user, password));
        }

        if (!await _userManager.IsInRoleAsync(user, role))
            EnsureSucceeded(await _userManager.AddToRoleAsync(user, role));
    }

    private static void EnsureSucceeded(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}

