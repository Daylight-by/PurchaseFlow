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
        IReadOnlyList<SupplierQuoteDto> quotes)
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
        result.Checks = new List<DecisionCheckDto>
        {
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
        result.IsReadyToSubmit = hasEnoughQuotes && hasSelectedQuote && hasReason;

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

        return result;
    }
}

