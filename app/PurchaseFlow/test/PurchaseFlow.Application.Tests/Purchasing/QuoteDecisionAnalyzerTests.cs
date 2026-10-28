using System;
using System.Linq;
using PurchaseFlow.Purchasing;
using Xunit;

namespace PurchaseFlow.Application.Tests.Purchasing;

public class QuoteDecisionAnalyzerTests
{
    [Fact]
    public void Empty_quotes_are_safe_and_fail_the_quote_check()
    {
        var result = QuoteDecisionAnalyzer.Analyze(100m, null, null, Array.Empty<SupplierQuoteDto>());

        Assert.Equal(0, result.QuoteCount);
        Assert.Null(result.CheapestAmount);
        Assert.Null(result.FastestDays);
        Assert.Empty(result.CheapestQuoteIds);
        Assert.Empty(result.FastestQuoteIds);
        Assert.Empty(result.DominatedQuoteIds);
        Assert.False(result.IsReadyToSubmit);
        Assert.False(result.Checks.Single(x => x.Code == "NeedTwoQuotes").IsComplete);
    }

    [Fact]
    public void Invalid_selection_and_blank_reason_are_reported_separately()
    {
        var quote = Quote(100m, 2);
        var result = QuoteDecisionAnalyzer.Analyze(200m, Guid.NewGuid(), "  ", new[] { quote, Quote(110m, 3) });

        Assert.False(result.Checks.Single(x => x.Code == "NeedSelectedQuote").IsComplete);
        Assert.False(result.Checks.Single(x => x.Code == "NeedSelectionReason").IsComplete);
        Assert.False(result.IsReadyToSubmit);
        Assert.Null(result.SelectedMarginPercent);
    }

    [Fact]
    public void Two_quotes_valid_selection_and_reason_are_ready()
    {
        var first = Quote(100m, 2);
        var second = Quote(110m, 3);

        var result = QuoteDecisionAnalyzer.Analyze(200m, first.Id, "价格更低", new[] { first, second });

        Assert.True(result.IsReadyToSubmit);
        Assert.All(result.Checks, check => Assert.True(check.IsComplete));
        Assert.Equal(50m, result.SelectedMarginPercent);
    }

    [Fact]
    public void Cheapest_and_fastest_support_ties()
    {
        var first = Quote(100m, 4);
        var second = Quote(100m, 2);
        var third = Quote(120m, 2);

        var result = QuoteDecisionAnalyzer.Analyze(200m, second.Id, "交期", new[] { first, second, third });

        Assert.Equal(100m, result.CheapestAmount);
        Assert.Equal(2, result.FastestDays);
        Assert.Equal(new[] { first.Id, second.Id }, result.CheapestQuoteIds);
        Assert.Equal(new[] { second.Id, third.Id }, result.FastestQuoteIds);
    }

    [Fact]
    public void Pareto_dominated_quotes_are_detected_without_marking_equal_tradeoffs()
    {
        var best = Quote(100m, 5);
        var tradeoff = Quote(90m, 8);
        var dominated = Quote(110m, 10);
        var sameAsBest = Quote(100m, 5);

        var result = QuoteDecisionAnalyzer.Analyze(200m, best.Id, "综合判断", new[] { best, tradeoff, dominated, sameAsBest });

        Assert.Contains(dominated.Id, result.DominatedQuoteIds);
        Assert.DoesNotContain(best.Id, result.DominatedQuoteIds);
        Assert.DoesNotContain(tradeoff.Id, result.DominatedQuoteIds);
        Assert.DoesNotContain(sameAsBest.Id, result.DominatedQuoteIds);
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == "SelectedQuoteDominated");
    }

    [Fact]
    public void Negative_margin_and_selected_dominance_are_warnings_only()
    {
        var dominating = Quote(120m, 3);
        var selected = Quote(150m, 5);

        var result = QuoteDecisionAnalyzer.Analyze(100m, selected.Id, "交期原因", new[] { dominating, selected });

        Assert.True(result.IsReadyToSubmit);
        Assert.Equal(-50m, result.SelectedMarginPercent);
        Assert.Contains(result.Warnings, warning => warning.Code == "NegativeExpectedGrossProfit");
        var dominanceWarning = Assert.Single(result.Warnings, warning => warning.Code == "SelectedQuoteDominated");
        Assert.Contains("此比较仅含价格和交期，不含质量服务", dominanceWarning.Message);
    }

    [Fact]
    public void Non_positive_sale_amount_does_not_divide_by_zero()
    {
        var quote = Quote(10m, 1);
        var result = QuoteDecisionAnalyzer.Analyze(0m, quote.Id, "理由", new[] { quote, Quote(12m, 2) });

        Assert.Null(result.SelectedMarginPercent);
    }

    [Fact]
    public void Selected_margin_is_rounded_to_two_decimal_places()
    {
        var selected = Quote(1m, 1);

        var result = QuoteDecisionAnalyzer.Analyze(3m, selected.Id, "比例", new[] { selected, Quote(2m, 2) });

        Assert.Equal(66.67m, result.SelectedMarginPercent);
    }

    private static SupplierQuoteDto Quote(decimal amount, int days) => new()
    {
        Id = Guid.NewGuid(),
        Amount = amount,
        DeliveryDays = days
    };
}

