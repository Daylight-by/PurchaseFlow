using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Security;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第二轮第 2 批的真实授权测试（不使用 AlwaysAllow）。
///
/// 为什么必须有这一类测试：采购应用服务与详情页在类/页面级要求 `Requests`，
/// 而 ABP 权限校验**不隐含父子关系**。如果管理员角色只被授予 `ApproveHighValue`
/// 而没有 `Requests`，部署后管理员会 403、高金额请求卡在二级待审。
/// 这里直接用 DBMigrator 同样的 DemoPermissionGrants 授权规则 + 真实权限检查器，
/// 证明：审核员能一级审批但没有二级权限；管理员既能访问采购接口，也能完成二级审批。
/// </summary>
public class ApprovalAuthorizationTests : PurchaseFlowTestBase<ReceiptAuthorizationTestModule>
{
    private const string DemoPassword = "Approval@Test2026";

    private readonly IPurchaseRequestAppService _requestService;
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionManager _permissionManager;
    private readonly SwitchableCurrentPrincipalAccessor _principalAccessor;

    public ApprovalAuthorizationTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _permissionManager = GetRequiredService<IPermissionManager>();
        _principalAccessor = GetRequiredService<SwitchableCurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task The_demo_permission_grants_let_the_admin_complete_a_second_level_approval_but_not_the_reviewer()
    {
        // 与 DbMigrator 完全相同的角色/权限授予规则（含管理员被显式授予 Requests）。
        await EnsureRoleAsync(DemoPermissionGrants.AdminRole);
        await EnsureRoleAsync(DemoPermissionGrants.BuyerRole);
        await EnsureRoleAsync(DemoPermissionGrants.ReviewerRole);
        await WithUnitOfWorkAsync(() => DemoPermissionGrants.GrantAsync(_permissionManager));

        var buyer = await EnsureUserAsync("approval.auth.buyer", DemoPermissionGrants.BuyerRole);
        var reviewer = await EnsureUserAsync("approval.auth.reviewer", DemoPermissionGrants.ReviewerRole);
        var admin = await EnsureUserAsync("approval.auth.admin", DemoPermissionGrants.AdminRole);

        var requestId = await CreateSubmittedHighValueRequestAsync(buyer);

        // 审核员：能访问采购需求并做一级审批，但没有二级权限。
        using (_principalAccessor.SwitchTo(reviewer.UserId, reviewer.UserName, reviewer.RoleName))
        {
            var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            Assert.Equal(PurchaseRequestStatus.PendingReview, detail.Status);

            await WithUnitOfWorkAsync(() => _requestService.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "审核员一级同意" }));

            var afterFirst = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, afterFirst.Status);

            // 权限边界在服务端：直接调用二级审批必须被授权层拒绝，而不是靠隐藏按钮。
            await Assert.ThrowsAsync<AbpAuthorizationException>(() =>
                WithUnitOfWorkAsync(() => _requestService.ApproveSecondLevelAsync(requestId,
                    new ReviewPurchaseRequestDto { Comment = "审核员越权二级" })));
        }

        // 管理员：能访问采购接口（证明 Requests 已授予），并完成二级审批。
        using (_principalAccessor.SwitchTo(admin.UserId, admin.UserName, admin.RoleName))
        {
            var dashboard = await WithUnitOfWorkAsync(() => _requestService.GetDashboardAsync());
            Assert.Equal(1, dashboard.PendingSecondApprovalCount);

            await WithUnitOfWorkAsync(() => _requestService.ApproveSecondLevelAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "管理员二级同意" }));

            var approved = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            Assert.Equal(PurchaseRequestStatus.Approved, approved.Status);
            var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);
            Assert.Equal(55000m, order.Amount);
            Assert.Equal(order.Lines.Sum(x => x.Subtotal), order.Amount);
        }
    }

    private async Task<Guid> CreateSubmittedHighValueRequestAsync((Guid UserId, string UserName, string RoleName) buyer)
    {
        using (_principalAccessor.SwitchTo(buyer.UserId, buyer.UserName, buyer.RoleName))
        {
            // 金额 60,000 > 阈值 50,000，走两级审批。
            var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = $"AUTH-APPR-{Guid.NewGuid():N}",
                Description = "授权测试：高金额两级审批",
                SaleAmount = 70000m,
                Lines = new List<CreatePurchaseRequestLineDto>
                {
                    new() { ItemName = "测试物料", Specification = "规格", Quantity = 1m, UnitPrice = 60000m }
                }
            }));
            var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            var line = detail.Lines.Single();

            var firstQuoteId = await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "授权供应商A",
                    DeliveryDays = 5,
                    Lines = new List<SupplierQuoteLineInputDto>
                    {
                        new() { RequestLineId = line.Id, UnitPrice = 55000m }
                    }
                }));
            await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "授权供应商B",
                    DeliveryDays = 6,
                    Lines = new List<SupplierQuoteLineInputDto>
                    {
                        new() { RequestLineId = line.Id, UnitPrice = 59000m }
                    }
                }));
            await WithUnitOfWorkAsync(() => _requestService.SelectQuoteAsync(requestId,
                new SelectSupplierQuoteDto { QuoteId = firstQuoteId, Reason = "价格更低" }));
            await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
            return requestId;
        }
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            if (await _roleManager.FindByNameAsync(roleName) is null)
            {
                var created = await _roleManager.CreateAsync(new IdentityRole(Guid.NewGuid(), roleName));
                Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
            }
        });
    }

    private async Task<(Guid UserId, string UserName, string RoleName)> EnsureUserAsync(
        string userName,
        string roleName)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user is null)
            {
                user = new IdentityUser(Guid.NewGuid(), userName, $"{userName}@example.invalid");
                var created = await _userManager.CreateAsync(user, DemoPassword);
                Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
            }

            if (!await _userManager.IsInRoleAsync(user, roleName))
            {
                var added = await _userManager.AddToRoleAsync(user, roleName);
                Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(x => x.Description)));
            }
        });

        var stored = await WithUnitOfWorkAsync(() => _userManager.FindByNameAsync(userName));
        Assert.NotNull(stored);
        return (stored!.Id, stored.UserName, roleName);
    }
}
