using System;
using System.Collections.Generic;
using System.Linq;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// Computes quote comparison facts and submission readiness without changing domain state.
/// </summary>
public static class QuoteDecisionAnalyzer
{
    public static QuoteDecisionAnalysisDto Analyze(
        decimal saleAmount,
        Guid? selectedId,
        string? reason,
        IReadOnlyList<SupplierQuoteDto> quotes,
        int lineCount = 0,
        decimal budgetAmount = 0m)
    {
        var items = quotes ?? Array.Empty<SupplierQuoteDto>();
        var result = new QuoteDecisionAnalysisDto
        {
            QuoteCount = items.Count
        };

        if (items.Count > 0)
        {
            result.CheapestAmount = items.Min(x => x.Amount);
            result.FastestDays = items.Min(x => x.DeliveryDays);
            result.CheapestQuoteIds = items
                .Where(x => x.Amount == result.CheapestAmount.Value)
                .Select(x => x.Id)
                .ToList();
            result.FastestQuoteIds = items
                .Where(x => x.DeliveryDays == result.FastestDays.Value)
                .Select(x => x.Id)
                .ToList();
            result.DominatedQuoteIds = items
                .Where(candidate => items.Any(other =>
                    other.Id != candidate.Id &&
                    other.Amount <= candidate.Amount &&
                    other.DeliveryDays <= candidate.DeliveryDays &&
                    (other.Amount < candidate.Amount || other.DeliveryDays < candidate.DeliveryDays)))
                .Select(x => x.Id)
                .ToList();
        }

        var selected = selectedId.HasValue
            ? items.FirstOrDefault(x => x.Id == selectedId.Value)
            : null;
        var hasSelectedQuote = selected is not null;
        if (hasSelectedQuote && saleAmount > 0)
        {
            result.SelectedMarginPercent = Math.Round(
                (saleAmount - selected!.Amount) / saleAmount * 100m,
                2,
                MidpointRounding.AwayFromZero);
        }

        var hasEnoughQuotes = items.Count >= 2;
        var hasReason = !string.IsNullOrWhiteSpace(reason);
        var hasLines = lineCount > 0;
        result.Checks = new List<DecisionCheckDto>
        {
            new()
            {
                Code = "NeedPurchaseLines",
                Label = "至少一条采购明细",
                IsComplete = hasLines
            },
            new()
            {
                Code = "NeedTwoQuotes",
                Label = "至少两条供应商报价",
                IsComplete = hasEnoughQuotes
            },
            new()
            {
                Code = "NeedSelectedQuote",
                Label = "已选择本需求中的报价",
                IsComplete = hasSelectedQuote
            },
            new()
            {
                Code = "NeedSelectionReason",
                Label = "已填写选择理由",
                IsComplete = hasReason
            }
        };
        result.IsReadyToSubmit = hasLines && hasEnoughQuotes && hasSelectedQuote && hasReason;

        if (hasSelectedQuote && saleAmount - selected!.Amount < 0)
        {
            result.Warnings.Add(new DecisionAlertDto
            {
                Code = "NegativeExpectedGrossProfit",
                Message = "所选报价会产生负预计毛利。"
            });
        }

        if (hasSelectedQuote && result.DominatedQuoteIds.Contains(selected!.Id))
        {
            result.Warnings.Add(new DecisionAlertDto
            {
                Code = "SelectedQuoteDominated",
                Message = "存在采购金额不更高且交期不更慢的报价；此比较仅含价格和交期，不含质量服务。"
            });
        }

        // P1-4：预算基准缺失时不能静默跳过超预算检查。预算金额来自采购明细汇总，
        // 为 0（或负数）意味着根本没有可比基准；若直接跳过，“无预算”看起来像
        // “不超预算”，会误导审批人。这里给出明确的“无法判定”警告。
        if (budgetAmount <= 0m)
        {
            result.Warnings.Add(new DecisionAlertDto
            {
                Code = "BudgetNotConfigured",
                Message = "采购明细汇总为 0，未设置有效预算基准，无法进行超预算判定。"
            });
        }

        if (budgetAmount > 0)
        {
            var overBudget = items.Count(x => x.Amount > budgetAmount);
            if (overBudget > 0)
            {
                result.Warnings.Add(new DecisionAlertDto
                {
                    Code = "QuoteOverBudget",
                    Message = $"有 {overBudget} 条报价高于采购明细汇总金额 ¥{budgetAmount:N2}（预算基准）。"
                });
            }

            if (hasSelectedQuote && selected!.Amount > budgetAmount)
            {
                result.Warnings.Add(new DecisionAlertDto
                {
                    Code = "SelectedQuoteOverBudget",
                    Message = $"所选报价高于采购明细汇总金额 ¥{budgetAmount:N2}（预算基准）。"
                });
            }
        }

        return result;
    }
}
