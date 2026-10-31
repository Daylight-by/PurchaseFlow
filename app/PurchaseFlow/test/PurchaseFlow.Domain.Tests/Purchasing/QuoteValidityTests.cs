using System;
using System.Collections.Generic;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 3 批：供应商报价有效期的领域规则。
/// 重点：UTC 日期边界（有效至当天、次日失效）、长期有效（null）、
/// 过期报价不可选择、不可提交，且规则由领域层强制（页面隐藏按钮只是 UI 便利）。
/// </summary>
public class QuoteValidityTests
{
    private static readonly DateTime Today = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_quote_without_validity_never_expires()
    {
        var (_, quote) = RequestWithQuote(validUntil: null);

        Assert.Null(quote.ValidUntil);
        Assert.False(quote.IsExpired(Today));
        Assert.False(quote.IsExpired(Today.AddYears(10)));
    }

    [Fact]
    public void A_quote_is_valid_through_the_whole_expiry_date_and_expires_the_next_day()
    {
        var (_, quote) = RequestWithQuote(Today);

        Assert.False(quote.IsExpired(Today));
        Assert.False(quote.IsExpired(Today.AddHours(23).AddMinutes(59)));
        Assert.True(quote.IsExpired(Today.AddDays(1)));
        Assert.True(quote.IsExpired(Today.AddDays(1).AddHours(8)));
    }

    [Fact]
    public void Selecting_an_expired_quote_is_rejected()
    {
        var (request, quote) = RequestWithQuote(Today);

        var exception = Assert.Throws<BusinessException>(() =>
            request.SelectQuote(quote.Id, "过期报价", Today.AddDays(1)));
        Assert.Equal(PurchaseFlowErrors.QuoteExpired, exception.Code);
        Assert.Null(request.SelectedQuoteId);
    }

    [Fact]
    public void Submitting_after_the_selected_quote_expired_is_rejected()
    {
        var request = NewRequest();
        var line = request.AddLine(Guid.NewGuid(), "物料", "规格", 1m, 100m);
        var selected = AddQuote(request, "供应商A", line.Id, 90m, Today);
        AddQuote(request, "供应商B", line.Id, 95m, validUntil: null);

        // 当天选择有效；但隔天提交时报价已过期，必须拒绝，防止用失效价格成交。
        request.SelectQuote(selected.Id, "价格更低", Today);

        var exception = Assert.Throws<BusinessException>(() => request.Submit(Today.AddDays(1)));
        Assert.Equal(PurchaseFlowErrors.QuoteExpired, exception.Code);
        Assert.Equal(PurchaseRequestStatus.Draft, request.Status);
    }

    [Fact]
    public void A_long_lived_quote_can_be_submitted_without_an_expiry_limit()
    {
        var request = NewRequest();
        var line = request.AddLine(Guid.NewGuid(), "物料", "规格", 1m, 100m);
        var selected = AddQuote(request, "供应商A", line.Id, 90m, validUntil: null);
        AddQuote(request, "供应商B", line.Id, 95m, validUntil: Today.AddYears(1));

        request.SelectQuote(selected.Id, "长期有效", Today);
        request.Submit(Today.AddYears(5));

        Assert.Equal(PurchaseRequestStatus.PendingReview, request.Status);
    }

    private static (PurchaseRequest Request, SupplierQuote Quote) RequestWithQuote(DateTime? validUntil)
    {
        var request = NewRequest();
        var line = request.AddLine(Guid.NewGuid(), "物料", "规格", 1m, 100m);
        var quote = AddQuote(request, "供应商A", line.Id, 90m, validUntil);
        return (request, quote);
    }

    private static SupplierQuote AddQuote(
        PurchaseRequest request,
        string supplierName,
        Guid requestLineId,
        decimal unitPrice,
        DateTime? validUntil)
    {
        return request.AddQuote(
            Guid.NewGuid(),
            supplierName,
            5,
            null,
            new List<(Guid Id, Guid RequestLineId, decimal UnitPrice)>
            {
                (Guid.NewGuid(), requestLineId, unitPrice)
            },
            supplierId: null,
            validUntil: validUntil);
    }

    private static PurchaseRequest NewRequest() =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "SO-QUOTE-VALIDITY", "报价有效期测试", 10000m);
}
