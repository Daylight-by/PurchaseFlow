using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PurchaseFlow.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace PurchaseFlow.DbMigrator;

/// <summary>
/// Creates reusable demo roles. Demo users are created only when their passwords
/// are supplied through local configuration, so no public repository credential
/// grants access to a newly deployed environment.
/// </summary>
public class DemoRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IConfiguration _configuration;

    public DemoRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IdentityUserManager userManager,
        IPermissionManager permissionManager,
        IGuidGenerator guidGenerator,
        IConfiguration configuration)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _permissionManager = permissionManager;
        _guidGenerator = guidGenerator;
        _configuration = configuration;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // Demo identities are host-only. Tenant administrators can define their
        // own roles and users without inheriting these sample accounts.
        if (context.TenantId.HasValue) return;

        await EnsureRoleAsync("buyer", PurchaseFlowPermissions.EditRequests);
        await EnsureRoleAsync("reviewer", PurchaseFlowPermissions.ReviewRequests);

        await EnsureUserAsync("demo.buyer", "buyer@example.invalid", "buyer",
            _configuration["Demo:BuyerPassword"]);
        await EnsureUserAsync("demo.reviewer", "reviewer@example.invalid", "reviewer",
            _configuration["Demo:ReviewerPassword"]);
    }

    private async Task EnsureRoleAsync(string name, string actionPermission)
    {
        if (await _roleManager.FindByNameAsync(name) is null)
            EnsureSucceeded(await _roleManager.CreateAsync(new IdentityRole(_guidGenerator.Create(), name)));

        await _permissionManager.SetForRoleAsync(name, PurchaseFlowPermissions.Requests, true);
        await _permissionManager.SetForRoleAsync(name, actionPermission, true);
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

