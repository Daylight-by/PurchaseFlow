using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Security;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第二轮第 2 批（金额分级审批）的端到端集成测试：
/// 高金额请求必须两级且换人，订单只在二级批准后生成；低金额仍是一级批准即出单；
/// 列表与仪表盘分别统计“待一级/待二级”数量。
/// </summary>
public class ApprovalFlowTests : PurchaseFlowTestBase<ApprovalFlowTestModule>
{
    private readonly IPurchaseRequestAppService _requests;
    private readonly SwitchableCurrentPrincipalAccessor _principalAccessor;
    private readonly IRepository<PurchaseOrder, Guid> _orders;

    public ApprovalFlowTests()
    {
        _requests = GetRequiredService<IPurchaseRequestAppService>();
        _principalAccessor = GetRequiredService<SwitchableCurrentPrincipalAccessor>();
        _orders = GetRequiredService<IRepository<PurchaseOrder, Guid>>();
    }

    [Fact]
    public async Task A_high_value_request_needs_two_approvers_and_only_then_creates_the_order()
    {
        var buyerId = Guid.NewGuid();
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();
        // 金额 60,000 > 50,000；两家报价 55,000 / 59,000，选中 55,000。
        var requestId = await CreateSubmittedRequestAsync(buyerId, 60000m, 55000m, 59000m);

        using (_principalAccessor.SwitchTo(firstApproverId, "reviewer.one", "reviewer"))
        {
            await WithUnitOfWorkAsync(() => _requests.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "一级同意" }));
        }

        var afterFirstLevel = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        Assert.True(afterFirstLevel.RequiresSecondApproval);
        Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, afterFirstLevel.Status);
        Assert.Equal(firstApproverId, afterFirstLevel.FirstApproverUserId);
        Assert.Null(afterFirstLevel.PurchaseOrder);
        Assert.Equal(0L, await WithUnitOfWorkAsync(() => _orders.GetCountAsync()));

        // 同一人不能完成二级审批。
        using (_principalAccessor.SwitchTo(firstApproverId, "reviewer.one", "reviewer"))
        {
            var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
                await WithUnitOfWorkAsync(() => _requests.ApproveSecondLevelAsync(requestId,
                    new ReviewPurchaseRequestDto { Comment = "我自己再批一次" })));
            Assert.Equal(PurchaseFlowErrors.SecondApprovalRequiresDifferentUser, exception.Code);
        }

        using (_principalAccessor.SwitchTo(secondApproverId, "admin.two", "admin"))
        {
            await WithUnitOfWorkAsync(() => _requests.ApproveSecondLevelAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "二级同意" }));
        }

        var approved = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        Assert.Equal(PurchaseRequestStatus.Approved, approved.Status);
        var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);
        Assert.Equal(55000m, order.Amount);
        Assert.Equal(order.Lines.Sum(x => x.Subtotal), order.Amount);
        Assert.Equal(1L, await WithUnitOfWorkAsync(() => _orders.GetCountAsync()));

        // 审批留痕：一级、二级分别记录，且操作人不同。
        Assert.Contains(approved.History, x => x.Action == "ApprovedLevel1" && x.ActorUserId == firstApproverId);
        Assert.Contains(approved.History, x => x.Action == "ApprovedSecondLevel" && x.ActorUserId == secondApproverId);
    }

    [Fact]
    public async Task A_low_value_request_is_approved_in_one_step_and_creates_the_order_immediately()
    {
        var buyerId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var requestId = await CreateSubmittedRequestAsync(buyerId, 20000m, 18000m, 19000m);

        using (_principalAccessor.SwitchTo(approverId, "reviewer.one", "reviewer"))
        {
            await WithUnitOfWorkAsync(() => _requests.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "低金额一级同意" }));
        }

        var approved = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        Assert.False(approved.RequiresSecondApproval);
        Assert.Equal(PurchaseRequestStatus.Approved, approved.Status);
        var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);
        Assert.Equal(18000m, order.Amount);
        Assert.Equal(order.Lines.Sum(x => x.Subtotal), order.Amount);
        Assert.Contains(approved.History, x => x.Action == "Approved" && x.ActorUserId == approverId);
    }

    [Fact]
    public async Task Lists_and_dashboard_separate_first_level_and_second_level_pending_work()
    {
        var buyerId = Guid.NewGuid();
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();
        var requestId = await CreateSubmittedRequestAsync(buyerId, 60000m, 55000m, 59000m);

        using (_principalAccessor.SwitchTo(firstApproverId, "reviewer.one", "reviewer"))
        {
            await WithUnitOfWorkAsync(() => _requests.ApproveAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "一级同意" }));
        }

        var dashboardAfterFirstLevel = await WithUnitOfWorkAsync(() => _requests.GetDashboardAsync());
        Assert.Equal(1, dashboardAfterFirstLevel.PendingSecondApprovalCount);
        Assert.Equal(0, dashboardAfterFirstLevel.PendingReviewCount);
        Assert.Equal(0, dashboardAfterFirstLevel.ApprovedCount);

        var listAfterFirstLevel = await WithUnitOfWorkAsync(() => _requests.GetListAsync(new PurchaseRequestListInput
        {
            Status = PurchaseRequestStatus.PendingSecondApproval
        }));
        Assert.Equal(1, listAfterFirstLevel.TotalCount);
        Assert.Equal(1, listAfterFirstLevel.PendingSecondApprovalCount);

        using (_principalAccessor.SwitchTo(secondApproverId, "admin.two", "admin"))
        {
            await WithUnitOfWorkAsync(() => _requests.ApproveSecondLevelAsync(requestId,
                new ReviewPurchaseRequestDto { Comment = "二级同意" }));
        }

        var dashboardAfterSecondLevel = await WithUnitOfWorkAsync(() => _requests.GetDashboardAsync());
        Assert.Equal(0, dashboardAfterSecondLevel.PendingSecondApprovalCount);
        Assert.Equal(1, dashboardAfterSecondLevel.ApprovedCount);
    }

    /// <summary>
    /// 以采购员身份创建并提交一个请求，再以审核员身份准备逐项报价。
    /// 报价单价即请求行的成交单价；金额高的请求走两级审批。
    /// </summary>
    private async Task<Guid> CreateSubmittedRequestAsync(
        Guid buyerId,
        decimal requestAmount,
        decimal firstQuoteAmount,
        decimal secondQuoteAmount)
    {
        using (_principalAccessor.SwitchTo(buyerId, "buyer.one", "buyer"))
        {
            var requestId = await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = $"APPR-{Guid.NewGuid():N}",
                Description = "分级审批测试",
                SaleAmount = requestAmount + 10000m,
                Lines = new List<CreatePurchaseRequestLineDto>
                {
                    new() { ItemName = "测试物料", Specification = "规格", Quantity = 1m, UnitPrice = requestAmount }
                }
            }));
            var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
            var line = detail.Lines.Single();

            var firstQuoteId = await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
            {
                SupplierName = "供应商A",
                DeliveryDays = 5,
                Lines = new List<SupplierQuoteLineInputDto>
                {
                    new() { RequestLineId = line.Id, UnitPrice = firstQuoteAmount }
                }
            }));
            await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId,
                new AddSupplierQuoteDto
                {
                    SupplierName = "供应商B",
                    DeliveryDays = 6,
                    Lines = new List<SupplierQuoteLineInputDto>
                    {
                        new() { RequestLineId = line.Id, UnitPrice = secondQuoteAmount }
                    }
                }));

            await WithUnitOfWorkAsync(() => _requests.SelectQuoteAsync(requestId, new SelectSupplierQuoteDto
            {
                QuoteId = firstQuoteId,
                Reason = "价格更低"
            }));
            await WithUnitOfWorkAsync(() => _requests.SubmitAsync(requestId));
            return requestId;
        }
    }
}
