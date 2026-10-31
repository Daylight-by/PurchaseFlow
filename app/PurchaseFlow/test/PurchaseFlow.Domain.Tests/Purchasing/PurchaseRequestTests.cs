using System;
using System.Collections.Generic;
using System.Linq;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace PurchaseFlow.Purchasing;

public class PurchaseRequestTests
{
    [Fact]
    public void Amount_is_derived_from_the_lines_and_never_typed_in()
    {
        var request = NewRequest();

        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        request.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 4.5m, 12m);

        // 10 * 25.5 = 255.00, 4.5 * 12 = 54.00
        Assert.Equal(309m, request.Amount);
        Assert.Equal(2, request.Lines.Count);
    }

    [Fact]
    public void Removing_a_line_updates_the_amount()
    {
        var request = NewRequest();
        var line = request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        request.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 2, 12m);

        request.RemoveLine(line.Id);

        Assert.Equal(24m, request.Amount);
        Assert.Single(request.Lines);
    }

    [Fact]
    public void Setting_lines_replaces_the_whole_set()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);

        request.SetLines(new[]
        {
            (Guid.NewGuid(), "深沟球轴承", "6205-2RS", 2m, 25.5m),
            (Guid.NewGuid(), "联轴器", "梅花型", 1m, 80m)
        });

        Assert.Equal(2, request.Lines.Count);
        Assert.Equal(131m, request.Amount);
    }

    [Fact]
    public void Rejected_line_does_not_change_the_existing_lines()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);

        Assert.Throws<BusinessException>(() => request.SetLines(new[]
        {
            (Guid.NewGuid(), "深沟球轴承", "6205-2RS", 2m, 25.5m),
            (Guid.NewGuid(), "联轴器", "梅花型", 0m, 80m)
        }));

        Assert.Single(request.Lines);
        Assert.Equal(255m, request.Amount);
    }

    [Fact]
    public void SetLines_rejects_duplicate_ids_without_changing_the_existing_lines()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        var duplicatedId = Guid.NewGuid();

        // P1-3: 同一条明细 Id 出现两次无法判定“更新还是新增”，必须显式拒绝。
        var exception = Assert.Throws<BusinessException>(() => request.SetLines(new[]
        {
            (duplicatedId, "联轴器", "梅花型", 1m, 80m),
            (duplicatedId, "链轮", "08B-1", 2m, 45m)
        }));

        Assert.Equal(PurchaseFlowErrors.DuplicateLineId, exception.Code);
        Assert.Single(request.Lines);
        Assert.Equal(255m, request.Amount);
    }

    [Fact]
    public void Submit_requires_lines_two_quotes_a_selected_quote_and_a_reason()
    {
        var request = NewRequest();

        Assert.Equal(PurchaseFlowErrors.NeedPurchaseLines,
            Assert.Throws<BusinessException>(() => request.Submit()).Code);

        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        Assert.Equal(PurchaseFlowErrors.NeedTwoQuotes,
            Assert.Throws<BusinessException>(() => request.Submit()).Code);

        var first = AddQuote(request, "华东机电", 25m, 7);
        Assert.Equal(PurchaseFlowErrors.NeedTwoQuotes,
            Assert.Throws<BusinessException>(() => request.Submit()).Code);

        AddQuote(request, "南方轴承", 27m, 5);
        Assert.Equal(PurchaseFlowErrors.NeedSelectedQuote,
            Assert.Throws<BusinessException>(() => request.Submit()).Code);

        // A selection without a reason is rejected before it can be submitted.
        Assert.Equal(PurchaseFlowErrors.NeedSelectionReason,
            Assert.Throws<BusinessException>(() => request.SelectQuote(first.Id, "   ")).Code);

        request.SelectQuote(first.Id, "价格更低，账期稳定");
        request.Submit();
        Assert.Equal(PurchaseRequestStatus.PendingReview, request.Status);
    }

    [Fact]
    public void A_quote_of_another_request_can_not_be_selected()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        var otherRequest = NewRequest();
        otherRequest.AddLine(Guid.NewGuid(), "电机", "YS-90", 2m, 350m);
        var otherQuote = AddQuote(otherRequest, "华东机电", 300m, 7);

        Assert.Throws<EntityNotFoundException>(() => request.SelectQuote(otherQuote.Id, "越权选择"));
    }

    [Fact]
    public void The_same_supplier_can_not_quote_twice()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        AddQuote(request, "华东机电", 25m, 7);

        Assert.Equal(PurchaseFlowErrors.DuplicateSupplier,
            Assert.Throws<BusinessException>(() => AddQuote(request, " 华东机电 ", 26m, 6)).Code);
    }

    [Fact]
    public void Removing_the_selected_quote_clears_the_selection()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        var first = AddQuote(request, "华东机电", 25m, 7);
        AddQuote(request, "南方轴承", 27m, 5);
        request.SelectQuote(first.Id, "价格更低");

        request.RemoveQuote(first.Id);

        Assert.Null(request.SelectedQuoteId);
        Assert.Null(request.SelectionReason);
        Assert.Single(request.Quotes);
    }

    [Fact]
    public void Submitted_request_is_frozen_until_returned()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        var quote = AddQuote(request, "华东机电", 25m, 7);
        AddQuote(request, "南方轴承", 27m, 5);
        request.SelectQuote(quote.Id, "交付更快");
        request.Submit();

        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.SetDetails("SO-2", "修改", 110000m)).Code);
        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.AddLine(Guid.NewGuid(), "联轴器", "梅花型", 1m, 80m)).Code);
        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => AddQuote(request, "北方机电", 30m, 9)).Code);

        request.Return();
        request.SetDetails("SO-2", "修改", 110000m);
        Assert.Equal(PurchaseRequestStatus.Returned, request.Status);
        Assert.Equal(110000m, request.SaleAmount);
    }

    [Fact]
    public void Approved_request_cannot_be_resubmitted_or_changed()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        var quote = AddQuote(request, "华东机电", 25m, 7);
        AddQuote(request, "南方轴承", 27m, 5);
        request.SelectQuote(quote.Id, "交付更快");
        request.Submit();
        request.Approve();

        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.Submit()).Code);
        Assert.Equal(PurchaseFlowErrors.NotEditable,
            Assert.Throws<BusinessException>(() => request.SelectQuote(quote.Id, "改选")).Code);
    }

    [Fact]
    public void Quote_amount_is_the_sum_of_its_itemized_lines()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        request.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 4.5m, 12m);

        // P0-1: 供应商逐项报价：10 * 20 + 4.5 * 10 = 245.00，报价总额由明细汇总。
        var quote = AddQuote(request, "华东机电", 20m, 7, 10m);

        Assert.Equal(245m, quote.Amount);
        Assert.Equal(2, quote.Lines.Count);
        Assert.Equal(quote.Lines.Sum(x => x.Subtotal), quote.Amount);
        Assert.Equal(new[] { "深沟球轴承", "骨架油封" },
            quote.Lines.Select(x => x.ItemName).ToArray());
        Assert.Equal(new[] { 20m, 10m }, quote.Lines.Select(x => x.UnitPrice).ToArray());
    }

    [Fact]
    public void Quote_must_cover_every_request_line_exactly_once()
    {
        var request = NewRequest();
        request.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10, 25.5m);
        request.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 4.5m, 12m);
        var firstLine = request.Lines.First();

        // 少报一条明细不允许。
        Assert.Equal(PurchaseFlowErrors.QuoteLineMismatch,
            Assert.Throws<BusinessException>(() => request.AddQuote(Guid.NewGuid(), "华东机电", 7, null,
                new List<(Guid, Guid, decimal)> { (Guid.NewGuid(), firstLine.Id, 20m) })).Code);

        // 同一条需求明细报两次也不允许。
        Assert.Equal(PurchaseFlowErrors.QuoteLineMismatch,
            Assert.Throws<BusinessException>(() => request.AddQuote(Guid.NewGuid(), "华东机电", 7, null,
                new List<(Guid, Guid, decimal)>
                {
                    (Guid.NewGuid(), firstLine.Id, 20m),
                    (Guid.NewGuid(), firstLine.Id, 21m)
                })).Code);

        Assert.Empty(request.Quotes);
    }

    private static SupplierQuote AddQuote(
        PurchaseRequest request,
        string supplier,
        decimal firstUnitPrice,
        int deliveryDays,
        decimal? secondUnitPrice = null,
        string? note = null)
    {
        var prices = request.Lines.Select((line, index) =>
            (Id: Guid.NewGuid(), RequestLineId: line.Id,
                UnitPrice: index == 0 ? firstUnitPrice : secondUnitPrice ?? firstUnitPrice)).ToList();
        return request.AddQuote(Guid.NewGuid(), supplier, deliveryDays, note, prices);
    }

    private static PurchaseRequest NewRequest() =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "SO-1", "采购需求", 100000m);
}
