using System;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
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

        await EnsureRoleAsync("buyer");
        await EnsureRoleAsync("reviewer");
        await EnsureRoleAsync("receiver");

        // 第二轮修复：演示角色的权限授予集中到 DemoPermissionGrants，
        // 由部署种子和真实授权测试共用，避免“管理员漏授父权限 Requests”
        // 这类问题只能在部署后被人工发现。
        await DemoPermissionGrants.GrantAsync(_permissionManager);

        // Demo accounts are created unconditionally with well-known demo
        // passwords, so a freshly deployed instance is directly usable for
        // evaluation (the credentials are shown on the login page).
        await EnsureUserAsync("demo.buyer", "buyer@example.invalid", "buyer", "Buyer@Demo123");
        await EnsureUserAsync("demo.reviewer", "reviewer@example.invalid", "reviewer", "Reviewer@Demo123");
        await EnsureUserAsync("demo.receiver", "receiver@example.invalid", "receiver",
            Environment.GetEnvironmentVariable("Demo__ReceiverPassword"));
    }

    /// <summary>
    /// 只负责创建角色；权限授予统一走 DemoPermissionGrants，避免两处口径漂移。
    /// </summary>
    private async Task EnsureRoleAsync(string name)
    {
        if (await _roleManager.FindByNameAsync(name) is null)
            EnsureSucceeded(await _roleManager.CreateAsync(new IdentityRole(_guidGenerator.Create(), name)));
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

