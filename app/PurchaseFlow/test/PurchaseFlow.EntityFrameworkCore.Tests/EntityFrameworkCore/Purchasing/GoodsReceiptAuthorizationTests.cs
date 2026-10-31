using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Security;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// Verifies the goods receipt permissions with the real ABP authorization
/// stack (real roles, real grants, real permission checker). The shared test
/// base always allows every authorization, so it cannot prove that the
/// server rejects a caller without the register permission.
/// </summary>
public class GoodsReceiptAuthorizationTests : PurchaseFlowTestBase<ReceiptAuthorizationTestModule>
{
    private const string DemoPassword = "Receipt@Test2026";

    private readonly IPurchaseRequestAppService _requestService;
    private readonly IGoodsReceiptAppService _receiptService;
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionManager _permissionManager;
    private readonly SwitchableCurrentPrincipalAccessor _principalAccessor;
    private readonly IRepository<GoodsReceipt, Guid> _receiptRepository;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLineRepository;

    public GoodsReceiptAuthorizationTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _permissionManager = GetRequiredService<IPermissionManager>();
        _principalAccessor = GetRequiredService<SwitchableCurrentPrincipalAccessor>();
        _receiptRepository = GetRequiredService<IRepository<GoodsReceipt, Guid>>();
        _orderLineRepository = GetRequiredService<IRepository<PurchaseOrderLine, Guid>>();
    }

    [Fact]
    public async Task A_role_without_the_register_permission_cannot_register_a_receipt()
    {
        var manager = await EnsureUserAsync("auth.manager", "auth-manager",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.EditRequests,
            PurchaseFlowPermissions.ReviewRequests,
            PurchaseFlowPermissions.ViewReceipts);
        var viewer = await EnsureUserAsync("auth.viewer", "auth-viewer",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.ViewReceipts);
        var buyer = await EnsureUserAsync("auth.buyer", "buyer",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.EditRequests,
            PurchaseFlowPermissions.ViewReceipts);
        var reviewer = await EnsureUserAsync("auth.reviewer", "reviewer",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.ReviewRequests,
            PurchaseFlowPermissions.ViewReceipts);

        var order = await CreateOrderAsync(manager);

        foreach (var user in new[] { viewer, buyer, reviewer })
        {
            using (_principalAccessor.SwitchTo(user.UserId, user.UserName, user.RoleName))
            {
                // Existing purchasing roles may read receipt progress but do not
                // gain registration permission as a side effect.
                var state = await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(order.Id));
                Assert.NotNull(state);

                // The server rejects a direct request; hiding the button is not
                // the authorization boundary.
                await Assert.ThrowsAsync<AbpAuthorizationException>(() =>
                    WithUnitOfWorkAsync(() => ReceiveAsync(order.Id, $"denied-{user.RoleName}", order, 4m)));
            }
        }

        var receiptCount = await WithUnitOfWorkAsync(() => _receiptRepository.GetCountAsync());
        Assert.Equal(0, receiptCount);

        var lines = await WithUnitOfWorkAsync(() => _orderLineRepository.GetListAsync(x => x.PurchaseOrderId == order.Id));
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(0m, line.ReceivedQuantity));
    }

    [Fact]
    public async Task A_role_with_the_register_permission_can_register_a_receipt()
    {
        var manager = await EnsureUserAsync("auth.manager", "auth-manager",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.EditRequests,
            PurchaseFlowPermissions.ReviewRequests,
            PurchaseFlowPermissions.ViewReceipts);
        var receiver = await EnsureUserAsync("auth.receiver", "auth-receiver",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.ViewReceipts,
            PurchaseFlowPermissions.RegisterReceipts);

        var order = await CreateOrderAsync(manager);

        using (_principalAccessor.SwitchTo(receiver.UserId, receiver.UserName, receiver.RoleName))
        {
            var receipt = await WithUnitOfWorkAsync(() => ReceiveAsync(order.Id, "receiver-key", order, 4m));

            // The operator comes from the signed-in user, never from the input.
            Assert.Equal(receiver.UserId, receipt.CreatorId);
            Assert.Equal("A", receipt.Lines.Single().ItemName);
            Assert.Equal(4m, receipt.Lines.Single().Quantity);

            var state = await WithUnitOfWorkAsync(() => _receiptService.GetOrderAsync(order.Id));
            Assert.Single(state.Receipts);
            Assert.True(state.IsPartiallyReceived);
            Assert.Equal(6m, state.Lines.Single(x => x.ItemName == "A").RemainingQuantity);
        }
    }

    [Fact]
    public async Task The_role_grants_reach_the_permission_checker()
    {
        var manager = await EnsureUserAsync("auth.manager", "auth-manager",
            PurchaseFlowPermissions.Requests,
            PurchaseFlowPermissions.EditRequests,
            PurchaseFlowPermissions.ViewReceipts);

        var storedUser = await WithUnitOfWorkAsync(() => _userManager.FindByNameAsync("auth.manager"));
        Assert.NotNull(storedUser);
        Assert.True(await _userManager.IsInRoleAsync(storedUser!, "auth-manager"), "The user is not in the role.");

        var counts = await WithUnitOfWorkAsync(ReadPermissionTableCountsAsync);
        Assert.True(counts.Grants > 0, "No permission grant row was written.");
        Assert.True(counts.UserRoles > 0, "No user role row was written.");

        using (_principalAccessor.SwitchTo(manager.UserId, manager.UserName, manager.RoleName))
        {
            var options = GetRequiredService<Microsoft.Extensions.Options.IOptions<PermissionManagementOptions>>().Value;
            var checker = GetRequiredService<IPermissionChecker>();
            var checkerType = checker.GetType().FullName;
            var providerManager = GetRequiredService<IPermissionValueProviderManager>();
            var providers = string.Join(", ", providerManager.ValueProviders.Select(x => x.GetType().Name));

            Assert.True(await checker.IsGrantedAsync(PurchaseFlowPermissions.EditRequests),
                $"The permission checker does not see the role grant. Grants: {counts.Grants}, UserRoles: {counts.UserRoles}, Roles: {counts.Roles}, " +
                $"checker: {checkerType}, providers: {providers}, dynamicStore: {options.IsDynamicPermissionStoreEnabled}, saveStatic: {options.SaveStaticPermissionsToDatabase}.");
        }
    }

    private async Task<(int Grants, int Roles, int UserRoles)> ReadPermissionTableCountsAsync()
    {
        var context = GetRequiredService<PurchaseFlowDbContext>();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        async Task<int> CountAsync(string table)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            var value = await command.ExecuteScalarAsync();
            return Convert.ToInt32(value);
        }

        var grants = await CountAsync("AbpPermissionGrants");
        var roles = await CountAsync("AbpRoles");
        var userRoles = await CountAsync("AbpUserRoles");
        return (grants, roles, userRoles);
    }

    [Fact]
    public async Task The_receipt_input_does_not_accept_an_operator_or_a_tenant_from_the_client()
    {
        var inputProperties = typeof(CreateGoodsReceiptDto).GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("CreatorId", inputProperties);
        Assert.DoesNotContain("UserId", inputProperties);
        Assert.DoesNotContain("TenantId", inputProperties);
        Assert.DoesNotContain("CurrentUser", inputProperties);

        await Task.CompletedTask;
    }

    private async Task<(Guid UserId, string UserName, string RoleName)> EnsureUserAsync(
        string userName,
        string roleName,
        params string[] permissions)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            if (await _roleManager.FindByNameAsync(roleName) is null)
            {
                var created = await _roleManager.CreateAsync(new IdentityRole(Guid.NewGuid(), roleName));
                Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
            }

            foreach (var permission in permissions)
                await _permissionManager.SetForRoleAsync(roleName, permission, true);

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

    private async Task<PurchaseOrderDto> CreateOrderAsync((Guid UserId, string UserName, string RoleName) operatorUser)
    {
        using (_principalAccessor.SwitchTo(operatorUser.UserId, operatorUser.UserName, operatorUser.RoleName))
        {
            var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = $"SO-{Guid.NewGuid():N}",
                Description = "授权测试订单",
                SaleAmount = 10000m,
                Lines = new List<CreatePurchaseRequestLineDto>
                {
                    new CreatePurchaseRequestLineDto
                    {
                        ItemName = "A",
                        Specification = "测试规格",
                        Quantity = 10m,
                        UnitPrice = 10m
                    }
                }
            }));

            // P0-1：报价改为逐项报价，报价总额仍为 100 / 110。
            var requestDetail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            var quoteId = await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "测试供应商",
                    DeliveryDays = 5,
                    Lines = QuoteLines(requestDetail, 10m)
                }));
            // The submission checklist needs at least two quotes to compare.
            await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "备用供应商",
                    DeliveryDays = 6,
                    Lines = QuoteLines(requestDetail, 11m)
                }));
            await WithUnitOfWorkAsync(() => _requestService.SelectQuoteAsync(requestId,
                new SelectSupplierQuoteDto { QuoteId = quoteId, Reason = "测试选择" }));
            await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
            await WithUnitOfWorkAsync(() => _requestService.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "测试批准" }));

            var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            return Assert.IsType<PurchaseOrderDto>(detail.PurchaseOrder);
        }
    }

    private async Task<GoodsReceiptDto> ReceiveAsync(
        Guid orderId,
        string key,
        PurchaseOrderDto order,
        decimal quantity)
    {
        var lineId = order.Lines.Single(line => line.ItemName == "A").Id;
        return await _receiptService.CreateAsync(new CreateGoodsReceiptDto
        {
            PurchaseOrderId = orderId,
            IdempotencyKey = key,
            ReceiptDate = new DateTime(2026, 10, 31),
            Note = "授权测试收货",
            Lines = new List<GoodsReceiptLineInputDto>
            {
                new GoodsReceiptLineInputDto
                {
                    PurchaseOrderLineId = lineId,
                    Quantity = quantity
                }
            }
        });
    }

    /// <summary>P0-1：把一条统一单价应用到当前需求的每条明细上。</summary>
    private static List<SupplierQuoteLineInputDto> QuoteLines(PurchaseRequestDetailDto detail, decimal unitPrice) =>
        detail.Lines.Select(line => new SupplierQuoteLineInputDto
        {
            RequestLineId = line.Id,
            UnitPrice = unitPrice
        }).ToList();
}
