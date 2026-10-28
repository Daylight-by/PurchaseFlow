using System;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

public class PurchaseRequestTests
{
    [Fact]
    public void Submit_requires_two_quotes_and_a_selected_quote()
    {
        var request = NewRequest();

        Assert.Equal(PurchaseFlowErrors.NeedTwoQuotes,
            Assert.Throws<BusinessException>(() => request.Submit(1)).Code);
        Assert.Equal(PurchaseFlowErrors.NeedSelectedQuote,
            Assert.Throws<BusinessException>(() => request.Submit(2)).Code);

        request.SelectQuote(Guid.NewGuid(), "交付更快");
        request.Submit(2);
        Assert.Equal(PurchaseRequestStatus.PendingReview, request.Status);
    }

    [Fact]
    public void Submitted_request_is_frozen_until_returned()
    {
        var request = NewRequest();
        request.SelectQuote(Guid.NewGuid(), "交付更快");
        request.Submit(2);

        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.SetDetails("SO-2", "修改", 110000m)).Code);

        request.Return();
        request.SetDetails("SO-2", "修改", 110000m);
        Assert.Equal(PurchaseRequestStatus.Returned, request.Status);
        Assert.Equal(110000m, request.SaleAmount);
    }

    [Fact]
    public void Approved_request_cannot_be_resubmitted_or_changed()
    {
        var request = NewRequest();
        request.SelectQuote(Guid.NewGuid(), "交付更快");
        request.Submit(2);
        request.Approve();

        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.Submit(2)).Code);
        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.SelectQuote(Guid.NewGuid(), "改选")).Code);
    }

    private static PurchaseRequest NewRequest() =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "SO-1", "采购需求", 100000m);
}

