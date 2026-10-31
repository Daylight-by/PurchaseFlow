using System;
using System.Collections.Generic;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 2 批：按金额分级审批的领域规则。
/// 重点：阈值边界（5 万）、一级批准后的状态路由、二级必须换人、越级/重复审批拒绝、
/// 退回后清空一级审批人并重新走完整流程。
/// </summary>
public class PurchaseApprovalTests
{
    [Fact]
    public void A_request_at_the_threshold_is_approved_by_one_level()
    {
        var request = SubmittedRequest(50000m);
        var approver = Guid.NewGuid();

        Assert.False(request.RequiresSecondApproval);
        Assert.Null(request.FirstApproverUserId);

        request.ApproveFirstLevel(approver);

        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
        Assert.Equal(approver, request.FirstApproverUserId);
    }

    [Fact]
    public void A_request_above_the_threshold_needs_a_second_approver_from_another_user()
    {
        var request = SubmittedRequest(50000.01m);
        var firstApprover = Guid.NewGuid();

        Assert.True(request.RequiresSecondApproval);

        request.ApproveFirstLevel(firstApprover);

        // 一级批准后不能直接变成已批准，必须等待二级审批，且一级审批人已记录。
        Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, request.Status);
        Assert.Equal(firstApprover, request.FirstApproverUserId);

        // 同一人不能既做一级又做二级（两级控制的核心）。
        var sameUser = Assert.Throws<BusinessException>(() => request.ApproveSecondLevel(firstApprover));
        Assert.Equal(PurchaseFlowErrors.SecondApprovalRequiresDifferentUser, sameUser.Code);
        Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, request.Status);

        request.ApproveSecondLevel(Guid.NewGuid());
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void The_second_level_can_not_run_before_the_first_level()
    {
        var request = SubmittedRequest(100000m);

        Assert.Equal(PurchaseFlowErrors.InvalidStatus,
            Assert.Throws<BusinessException>(() => request.ApproveSecondLevel(Guid.NewGuid())).Code);
        Assert.Equal(PurchaseRequestStatus.PendingReview, request.Status);
    }

    [Fact]
    public void A_low_value_request_can_not_be_approved_a_second_time()
    {
        var request = SubmittedRequest(1000m);
        request.ApproveFirstLevel(Guid.NewGuid());
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);

        Assert.Equal(PurchaseFlowErrors.InvalidStatus,
            Assert.Throws<BusinessException>(() => request.ApproveSecondLevel(Guid.NewGuid())).Code);
    }

    [Fact]
    public void Returning_from_the_second_level_clears_the_first_approver_and_resubmission_restarts_both_levels()
    {
        var request = SubmittedRequest(100000m);
        var firstApprover = Guid.NewGuid();
        request.ApproveFirstLevel(firstApprover);
        Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, request.Status);

        request.Return();

        Assert.Equal(PurchaseRequestStatus.Returned, request.Status);
        Assert.Null(request.FirstApproverUserId);

        request.SetDetails("SO-2-RETURNED", "退回后补充", 200000m);
        request.Submit();
        Assert.Equal(PurchaseRequestStatus.PendingReview, request.Status);
        Assert.Null(request.FirstApproverUserId);

        // 重新提交后必须重新走一级，旧的一级审批人不能自动继承。
        var newFirstApprover = Guid.NewGuid();
        request.ApproveFirstLevel(newFirstApprover);
        Assert.Equal(PurchaseRequestStatus.PendingSecondApproval, request.Status);
        Assert.Equal(newFirstApprover, request.FirstApproverUserId);
        request.ApproveSecondLevel(Guid.NewGuid());
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
    }

    /// <summary>
    /// 构造一个已提交的请求：单行金额 = amount（报价行金额取更小的值，避免影响阈值判断）。
    /// </summary>
    private static PurchaseRequest SubmittedRequest(decimal amount)
    {
        var request = new PurchaseRequest(Guid.NewGuid(), null, Guid.NewGuid(),
            "SO-APPROVAL", "分级审批测试", 200000m);
        var line = request.AddLine(Guid.NewGuid(), "测试物料", "规格", 1m, amount);
        var first = request.AddQuote(Guid.NewGuid(), "供应商A", 5, null,
            new List<(Guid, Guid, decimal)> { (Guid.NewGuid(), line.Id, 1000m) });
        request.AddQuote(Guid.NewGuid(), "供应商B", 6, null,
            new List<(Guid, Guid, decimal)> { (Guid.NewGuid(), line.Id, 1100m) });
        request.SelectQuote(first.Id, "价格更低");
        request.Submit();
        return request;
    }
}
