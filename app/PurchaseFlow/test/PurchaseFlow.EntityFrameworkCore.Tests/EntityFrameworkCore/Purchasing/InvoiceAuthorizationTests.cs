using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Security;
using Volo.Abp.Authorization;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第三轮：发票/付款权限的真实授权测试（不使用 AlwaysAllow）。
/// 验证 DemoPermissionGrants 的职责分离：采购员可登记发票/申请付款，
/// 审核员只能查看，只有管理员能批准和登记付款；服务端直接调用也必须被拒绝。
/// </summary>
public class InvoiceAuthorizationTests : PurchaseFlowTestBase<ReceiptAuthorizationTestModule>
{
    private const string DemoPassword = "Invoice@Test2026";

    private readonly IPurchaseRequestAppService _requestService;
    private readonly IGoodsReceiptAppService _receiptService;
    private readonly ISupplierInvoiceAppService _invoiceService;
    private readonly IPaymentAppService _paymentService;
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionManager _permissionManager;
    private readonly SwitchableCurrentPrincipalAccessor _principalAccessor;

    public InvoiceAuthorizationTests()
    {
        _requestService = GetRequiredService<IPurchaseRequestAppService>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
        _invoiceService = GetRequiredService<ISupplierInvoiceAppService>();
        _paymentService = GetRequiredService<IPaymentAppService>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _permissionManager = GetRequiredService<IPermissionManager>();
        _principalAccessor = GetRequiredService<SwitchableCurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task Invoice_and_payment_permissions_follow_the_register_approve_separation()
    {
        await EnsureRoleAsync(DemoPermissionGrants.AdminRole);
        await EnsureRoleAsync(DemoPermissionGrants.BuyerRole);
        await EnsureRoleAsync(DemoPermissionGrants.ReviewerRole);
        await WithUnitOfWorkAsync(() => DemoPermissionGrants.GrantAsync(_permissionManager));

        var admin = await EnsureUserAsync("invoice.auth.admin", DemoPermissionGrants.AdminRole);
        var buyer = await EnsureUserAsync("invoice.auth.buyer", DemoPermissionGrants.BuyerRole);
        var reviewer = await EnsureUserAsync("invoice.auth.reviewer", DemoPermissionGrants.ReviewerRole);

        // 管理员准备：低金额订单 + 收货 6 件（管理员拥有全部业务权限）。
        var order = await CreateReceivedOrderAsync(admin);

        // 采购员：可以登记发票并申请付款。
        Guid invoiceId;
        Guid paymentId;
        using (_principalAccessor.SwitchTo(buyer.UserId, buyer.UserName, buyer.RoleName))
        {
            var invoice = await WithUnitOfWorkAsync(() => _invoiceService.RegisterAsync(Invoice(order, "INV-AUTH-001", 4m)));
            invoiceId = invoice.Id;

            var payment = await WithUnitOfWorkAsync(() => _paymentService.RequestAsync(invoiceId,
                new CreatePaymentApplicationDto { Note = "采购员申请付款" }));
            paymentId = payment.Id;
        }

        // 审核员：只能查看，不能登记发票，也不能申请付款。
        using (_principalAccessor.SwitchTo(reviewer.UserId, reviewer.UserName, reviewer.RoleName))
        {
            var invoices = await WithUnitOfWorkAsync(() => _invoiceService.GetOrderInvoicesAsync(order.OrderId));
            Assert.Contains(invoices, x => x.Id == invoiceId);

            await Assert.ThrowsAsync<AbpAuthorizationException>(() =>
                WithUnitOfWorkAsync(() => _invoiceService.RegisterAsync(Invoice(order, "INV-AUTH-REVIEWER", 1m))));
            await Assert.ThrowsAsync<AbpAuthorizationException>(() =>
                WithUnitOfWorkAsync(() => _paymentService.RequestAsync(invoiceId,
                    new CreatePaymentApplicationDto())));
        }

        // 采购员也不能批准付款（审批权限只在管理员）。
        using (_principalAccessor.SwitchTo(buyer.UserId, buyer.UserName, buyer.RoleName))
        {
            await Assert.ThrowsAsync<AbpAuthorizationException>(() =>
                WithUnitOfWorkAsync(() => _paymentService.ApproveAsync(paymentId,
                    new ReviewPaymentApplicationDto())));
        }

        // 管理员：批准 + 登记付款；发票同步已付款。
        using (_principalAccessor.SwitchTo(admin.UserId, admin.UserName, admin.RoleName))
        {
            var approved = await WithUnitOfWorkAsync(() => _paymentService.ApproveAsync(paymentId,
                new ReviewPaymentApplicationDto { Comment = "管理员批准" }));
            Assert.Equal(PaymentApplicationStatus.Approved, approved.Status);

            var paid = await WithUnitOfWorkAsync(() => _paymentService.RegisterPaymentAsync(paymentId,
                new RegisterPaymentDto { PaymentDate = DateTime.UtcNow.Date, Note = "已付款" }));
            Assert.Equal(PaymentApplicationStatus.Paid, paid.Status);

            // GetAsync 也有 [Authorize(ViewInvoices)]，必须保持管理员身份读取最终状态。
            var paidInvoice = await WithUnitOfWorkAsync(() => _invoiceService.GetAsync(invoiceId));
            Assert.Equal(SupplierInvoiceStatus.Paid, paidInvoice.Status);
            Assert.Equal(PaymentApplicationStatus.Paid, paidInvoice.PaymentStatus);
        }
    }

    private static CreateSupplierInvoiceDto Invoice(
        (Guid OrderId, Guid OrderLineId, string OrderNumber) order,
        string invoiceNumber,
        decimal quantity) => new()
    {
        PurchaseOrderId = order.OrderId,
        InvoiceNumber = invoiceNumber,
        InvoiceDate = DateTime.UtcNow.Date,
        Lines = new List<CreateSupplierInvoiceLineDto>
        {
            new() { PurchaseOrderLineId = order.OrderLineId, Quantity = quantity, UnitPrice = 90m }
        }
    };

    private async Task<(Guid OrderId, Guid OrderLineId, string OrderNumber)> CreateReceivedOrderAsync(
        (Guid UserId, string UserName, string RoleName) operatorUser)
    {
        using (_principalAccessor.SwitchTo(operatorUser.UserId, operatorUser.UserName, operatorUser.RoleName))
        {
            var requestId = await WithUnitOfWorkAsync(() => _requestService.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = $"INV-AUTH-{Guid.NewGuid():N}",
                Description = "发票权限测试订单",
                SaleAmount = 10000m,
                Lines = new List<CreatePurchaseRequestLineDto>
                {
                    new() { ItemName = "办公椅", Specification = "人体工学", Quantity = 10m, UnitPrice = 100m }
                }
            }));
            var detail = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            var line = detail.Lines.Single();

            var quoteId = await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "发票权限供应商",
                    DeliveryDays = 5,
                    Lines = new List<SupplierQuoteLineInputDto>
                    {
                        new() { RequestLineId = line.Id, UnitPrice = 90m }
                    }
                }));
            await WithUnitOfWorkAsync(() => _requestService.AddQuoteAsync(requestId, new AddSupplierQuoteDto
            {
                SupplierName = "备选供应商",
                DeliveryDays = 7,
                Lines = new List<SupplierQuoteLineInputDto>
                {
                    new() { RequestLineId = line.Id, UnitPrice = 95m }
                }
            }));
            await WithUnitOfWorkAsync(() => _requestService.SelectQuoteAsync(requestId,
                new SelectSupplierQuoteDto { QuoteId = quoteId, Reason = "价格更低" }));
            await WithUnitOfWorkAsync(() => _requestService.SubmitAsync(requestId));
            await WithUnitOfWorkAsync(() => _requestService.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "低金额一级批准" }));

            var approved = await WithUnitOfWorkAsync(() => _requestService.GetAsync(requestId));
            var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);

            await WithUnitOfWorkAsync(() => _receiptService.CreateAsync(new CreateGoodsReceiptDto
            {
                PurchaseOrderId = order.Id,
                IdempotencyKey = $"inv-auth-receipt-{Guid.NewGuid():N}",
                ReceiptDate = DateTime.UtcNow.Date,
                Note = "发票权限测试收货",
                Lines = new List<GoodsReceiptLineInputDto>
                {
                    new() { PurchaseOrderLineId = order.Lines.Single().Id, Quantity = 6m }
                }
            }));

            return (order.Id, order.Lines.Single().Id, order.OrderNumber);
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
