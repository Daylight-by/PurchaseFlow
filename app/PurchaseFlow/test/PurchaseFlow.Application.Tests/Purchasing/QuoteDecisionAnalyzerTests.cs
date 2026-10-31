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
        var result = QuoteDecisionAnalyzer.Analyze(100m, null, null, Array.Empty<SupplierQuoteDto>(), 1);

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
        var result = QuoteDecisionAnalyzer.Analyze(200m, Guid.NewGuid(), "  ", new[] { quote, Quote(110m, 3) }, 1);

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

        var result = QuoteDecisionAnalyzer.Analyze(200m, first.Id, "价格更低", new[] { first, second }, 1);

        Assert.True(result.IsReadyToSubmit);
        Assert.All(result.Checks, check => Assert.True(check.IsComplete));
        Assert.Equal(50m, result.SelectedMarginPercent);
    }

    [Fact]
    public void A_request_without_lines_is_not_ready_to_submit()
    {
        var first = Quote(100m, 2);
        var second = Quote(110m, 3);

        var result = QuoteDecisionAnalyzer.Analyze(200m, first.Id, "价格更低", new[] { first, second }, 0);

        Assert.False(result.Checks.Single(x => x.Code == "NeedPurchaseLines").IsComplete);
        Assert.False(result.IsReadyToSubmit);
    }

    [Fact]
    public void Cheapest_and_fastest_support_ties()
    {
        var first = Quote(100m, 4);
        var second = Quote(100m, 2);
        var third = Quote(120m, 2);

        var result = QuoteDecisionAnalyzer.Analyze(200m, second.Id, "交期", new[] { first, second, third }, 1);

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

        var result = QuoteDecisionAnalyzer.Analyze(200m, best.Id, "综合判断", new[] { best, tradeoff, dominated, sameAsBest }, 1);

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

        var result = QuoteDecisionAnalyzer.Analyze(100m, selected.Id, "交期原因", new[] { dominating, selected }, 1);

        Assert.True(result.IsReadyToSubmit);
        Assert.Equal(-50m, result.SelectedMarginPercent);
        Assert.Contains(result.Warnings, warning => warning.Code == "NegativeExpectedGrossProfit");
        var dominanceWarning = Assert.Single(result.Warnings, warning => warning.Code == "SelectedQuoteDominated");
        Assert.Contains("此比较仅含价格和交期，不含质量服务", dominanceWarning.Message);
    }

    [Fact]
    public void Quotes_above_the_line_total_are_warned_about()
    {
        var withinBudget = Quote(90m, 5);
        var overBudget = Quote(130m, 3);

        var result = QuoteDecisionAnalyzer.Analyze(200m, overBudget.Id, "交期原因",
            new[] { withinBudget, overBudget }, 1, 100m);

        var budgetWarning = Assert.Single(result.Warnings, warning => warning.Code == "QuoteOverBudget");
        Assert.Contains("1 条报价高于采购明细汇总金额", budgetWarning.Message);
        Assert.Contains(result.Warnings, warning => warning.Code == "SelectedQuoteOverBudget");
        // Warnings never block the submission.
        Assert.True(result.IsReadyToSubmit);
    }

    [Fact]
    public void Missing_or_zero_budget_is_reported_instead_of_silently_skipped()
    {
        var first = Quote(100m, 2);
        var second = Quote(110m, 3);

        // P1-4: 预算基准为 0/未设置时不能静默跳过超预算检查，
        // 必须给出“无法判定”的明确提示；同时不应误报超预算。
        var result = QuoteDecisionAnalyzer.Analyze(200m, first.Id, "价格更低", new[] { first, second }, 1);

        Assert.Contains(result.Warnings, warning => warning.Code == "BudgetNotConfigured");
        var budgetWarning = Assert.Single(result.Warnings, warning => warning.Code == "BudgetNotConfigured");
        Assert.Contains("无法进行超预算判定", budgetWarning.Message);
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == "QuoteOverBudget");
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == "SelectedQuoteOverBudget");
        // The warning never blocks the submission.
        Assert.True(result.IsReadyToSubmit);
    }

    [Fact]
    public void Non_positive_sale_amount_does_not_divide_by_zero()
    {
        var quote = Quote(10m, 1);
        var result = QuoteDecisionAnalyzer.Analyze(0m, quote.Id, "理由", new[] { quote, Quote(12m, 2) }, 1);

        Assert.Null(result.SelectedMarginPercent);
    }

    [Fact]
    public void Selected_margin_is_rounded_to_two_decimal_places()
    {
        var selected = Quote(1m, 1);

        var result = QuoteDecisionAnalyzer.Analyze(3m, selected.Id, "比例", new[] { selected, Quote(2m, 2) }, 1);

        Assert.Equal(66.67m, result.SelectedMarginPercent);
    }

    private static SupplierQuoteDto Quote(decimal amount, int days) => new()
    {
        Id = Guid.NewGuid(),
        Amount = amount,
        DeliveryDays = days
    };
}
