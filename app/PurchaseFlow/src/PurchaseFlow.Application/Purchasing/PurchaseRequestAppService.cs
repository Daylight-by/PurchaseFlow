using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace PurchaseFlow.Purchasing;

[Authorize(PurchaseFlowPermissions.Requests)]
public class PurchaseRequestAppService : ApplicationService, IPurchaseRequestAppService
{
    private readonly IRepository<PurchaseRequest, Guid> _requests;
    private readonly IRepository<SupplierQuote, Guid> _quotes;
    private readonly IRepository<ReviewEvent, Guid> _events;
    private readonly IdentityUserManager _userManager;

    public PurchaseRequestAppService(
        IRepository<PurchaseRequest, Guid> requests,
        IRepository<SupplierQuote, Guid> quotes,
        IRepository<ReviewEvent, Guid> events,
        IdentityUserManager userManager)
    {
        _requests = requests;
        _quotes = quotes;
        _events = events;
        _userManager = userManager;
    }

    public async Task<PurchaseRequestListDto> GetListAsync(PurchaseRequestListInput input)
    {
        var query = await _requests.GetQueryableAsync();
        var counts = await AsyncExecuter.ToListAsync(query.GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }));
        var result = new PurchaseRequestListDto
        {
            AllCount = counts.Sum(x => x.Count),
            DraftCount = counts.Where(x => x.Status == PurchaseRequestStatus.Draft).Sum(x => x.Count),
            PendingReviewCount = counts.Where(x => x.Status == PurchaseRequestStatus.PendingReview).Sum(x => x.Count),
            ReturnedCount = counts.Where(x => x.Status == PurchaseRequestStatus.Returned).Sum(x => x.Count),
            ApprovedCount = counts.Where(x => x.Status == PurchaseRequestStatus.Approved).Sum(x => x.Count),
            PageSize = Math.Clamp(input.PageSize, 1, 50)
        };

        if (input.Status.HasValue)
            query = query.Where(x => x.Status == input.Status.Value);
        var keyword = input.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x => x.OrderReference.Contains(keyword) || x.Description.Contains(keyword));

        result.TotalCount = await AsyncExecuter.CountAsync(query);
        var lastPage = Math.Max(1, (result.TotalCount + result.PageSize - 1) / result.PageSize);
        result.Page = Math.Clamp(input.Page, 1, lastPage);
        var requests = await AsyncExecuter.ToListAsync(query
            .OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id)
            .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize));
        result.Items = requests.Select(MapSummary).ToList();
        return result;
    }

    public async Task<PurchaseDashboardDto> GetDashboardAsync()
    {
        var requestsQuery = await _requests.GetQueryableAsync();
        requestsQuery = requestsQuery.Where(x => x.TenantId == CurrentTenant.Id);
        var counts = await AsyncExecuter.ToListAsync(requestsQuery
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }));
        var result = new PurchaseDashboardDto
        {
            TotalCount = counts.Sum(x => x.Count),
            DraftCount = counts.Where(x => x.Status == PurchaseRequestStatus.Draft).Sum(x => x.Count),
            PendingReviewCount = counts.Where(x => x.Status == PurchaseRequestStatus.PendingReview).Sum(x => x.Count),
            ReturnedCount = counts.Where(x => x.Status == PurchaseRequestStatus.Returned).Sum(x => x.Count),
            ApprovedCount = counts.Where(x => x.Status == PurchaseRequestStatus.Approved).Sum(x => x.Count)
        };

        var quotesQuery = await _quotes.GetQueryableAsync();
        quotesQuery = quotesQuery.Where(x => x.TenantId == CurrentTenant.Id);
        result.TotalQuoteCount = await AsyncExecuter.CountAsync(quotesQuery);

        var approvedRequests = await AsyncExecuter.ToListAsync(requestsQuery
            .Where(x => x.Status == PurchaseRequestStatus.Approved));
        var selectedQuoteIds = approvedRequests
            .Where(x => x.SelectedQuoteId.HasValue)
            .Select(x => x.SelectedQuoteId!.Value)
            .Distinct()
            .ToList();
        if (selectedQuoteIds.Count > 0)
        {
            var approvedQuotes = await AsyncExecuter.ToListAsync(quotesQuery
                .Where(x => selectedQuoteIds.Contains(x.Id)));
            var approvedQuotesById = approvedQuotes.ToDictionary(x => x.Id);
            foreach (var request in approvedRequests)
            {
                if (request.SelectedQuoteId is not { } selectedQuoteId ||
                    !approvedQuotesById.TryGetValue(selectedQuoteId, out var quote) ||
                    quote.PurchaseRequestId != request.Id)
                    continue;

                result.ApprovedPurchaseAmount += quote.Amount;
                result.ApprovedExpectedGrossProfit += request.SaleAmount - quote.Amount;
            }
        }

        var pendingReviews = await AsyncExecuter.ToListAsync(requestsQuery
            .Where(x => x.Status == PurchaseRequestStatus.PendingReview)
            .OrderBy(x => x.CreationTime)
            .ThenBy(x => x.Id)
            .Take(5));
        result.PendingReviews = pendingReviews.Select(MapSummary).ToList();

        var recentRequests = await AsyncExecuter.ToListAsync(requestsQuery
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id)
            .Take(5));
        result.RecentRequests = recentRequests.Select(MapSummary).ToList();
        return result;
    }

    public async Task<PurchaseRequestDetailDto> GetAsync(Guid id)
    {
        var request = await _requests.GetAsync(id);
        var quotes = await GetQuotesAsync(id);
        var eventsQuery = await _events.GetQueryableAsync();
        var events = await AsyncExecuter.ToListAsync(eventsQuery.Where(x => x.PurchaseRequestId == id)
            .OrderBy(x => x.OccurredAtUtc));
        var actorNames = new Dictionary<Guid, string>();
        foreach (var actorId in events.Select(x => x.ActorUserId).Distinct())
            actorNames[actorId] = (await _userManager.FindByIdAsync(actorId.ToString()))?.UserName
                ?? "已删除用户";
        var selected = quotes.FirstOrDefault(x => x.Id == request.SelectedQuoteId);
        var cheapest = quotes.FirstOrDefault();
        var quoteDtos = quotes.Select(x => new SupplierQuoteDto
        {
            Id = x.Id,
            SupplierName = x.SupplierName,
            Amount = x.Amount,
            DeliveryDays = x.DeliveryDays,
            Note = x.Note,
            ExpectedGrossProfit = request.SaleAmount - x.Amount,
            CostPremiumToLowest = cheapest is null ? 0 : x.Amount - cheapest.Amount
        }).ToList();

        return new PurchaseRequestDetailDto
        {
            Id = request.Id,
            OrderReference = request.OrderReference,
            Description = request.Description,
            SaleAmount = request.SaleAmount,
            Status = request.Status,
            BuyerUserId = request.BuyerUserId,
            SelectedQuoteId = request.SelectedQuoteId,
            SelectionReason = request.SelectionReason,
            CreationTime = request.CreationTime,
            SelectedQuoteAmount = selected?.Amount,
            ExpectedGrossProfit = selected is null ? null : request.SaleAmount - selected.Amount,
            SelectedCostPremiumToLowest = selected is null || cheapest is null
                ? null : selected.Amount - cheapest.Amount,
            SelectedDeliveryDaysComparedWithLowest = selected is null || cheapest is null
                ? null : selected.DeliveryDays - cheapest.DeliveryDays,
            Quotes = quoteDtos,
            Decision = QuoteDecisionAnalyzer.Analyze(
                request.SaleAmount,
                request.SelectedQuoteId,
                request.SelectionReason,
                quoteDtos),
            History = events.Select(x => new ReviewEventDto
            {
                Id = x.Id,
                ActorUserId = x.ActorUserId,
                ActorName = actorNames[x.ActorUserId],
                Action = x.Action,
                Comment = x.Comment,
                OccurredAtUtc = x.OccurredAtUtc
            }).ToList()
        };
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task<Guid> CreateAsync(CreatePurchaseRequestDto input)
    {
        await EnsureUniqueOrderReferenceAsync(input.OrderReference);
        var request = new PurchaseRequest(GuidGenerator.Create(), CurrentTenant.Id, UserId(),
            input.OrderReference, input.Description, input.SaleAmount);
        await _requests.InsertAsync(request);
        return request.Id;
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task UpdateAsync(Guid id, UpdatePurchaseRequestDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        await EnsureUniqueOrderReferenceAsync(input.OrderReference, id);
        request.SetDetails(input.OrderReference, input.Description, input.SaleAmount);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task<Guid> AddQuoteAsync(Guid id, AddSupplierQuoteDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        request.EnsureEditable();
        if ((await GetQuotesAsync(id)).Any(x =>
                string.Equals(x.SupplierName, input.SupplierName?.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException(PurchaseFlowErrors.DuplicateSupplier);
        var quote = new SupplierQuote(GuidGenerator.Create(), CurrentTenant.Id, id,
            input.SupplierName, input.Amount, input.DeliveryDays, input.Note);
        await _quotes.InsertAsync(quote);
        return quote.Id;
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task DeleteQuoteAsync(Guid id, Guid quoteId)
    {
        var request = await GetOwnedRequestAsync(id);
        request.EnsureEditable();
        var quote = (await GetQuotesAsync(id)).FirstOrDefault(x => x.Id == quoteId)
            ?? throw new EntityNotFoundException(typeof(SupplierQuote), quoteId);
        request.ClearSelectedQuote(quoteId);
        await _quotes.DeleteAsync(quote);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task SelectQuoteAsync(Guid id, SelectSupplierQuoteDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        request.EnsureEditable();
        if (!(await GetQuotesAsync(id)).Any(x => x.Id == input.QuoteId))
            throw new EntityNotFoundException(typeof(SupplierQuote), input.QuoteId);
        request.SelectQuote(input.QuoteId, input.Reason);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task SubmitAsync(Guid id)
    {
        var request = await GetOwnedRequestAsync(id);
        var quotes = await GetQuotesAsync(id);
        if (request.SelectedQuoteId is not null && quotes.All(x => x.Id != request.SelectedQuoteId))
            throw new BusinessException(PurchaseFlowErrors.NeedSelectedQuote);
        request.Submit(quotes.Count);
        await _requests.UpdateAsync(request);
        await AddEventAsync(id, "Submitted", request.SelectionReason);
    }

    [Authorize(PurchaseFlowPermissions.ReviewRequests)]
    public async Task ApproveAsync(Guid id, ReviewPurchaseRequestDto input)
    {
        var request = await _requests.GetAsync(id);
        request.Approve();
        await _requests.UpdateAsync(request);
        await AddEventAsync(id, "Approved", input.Comment);
    }

    [Authorize(PurchaseFlowPermissions.ReviewRequests)]
    public async Task ReturnAsync(Guid id, ReviewPurchaseRequestDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Comment))
            throw new BusinessException(PurchaseFlowErrors.NeedReviewComment);
        var request = await _requests.GetAsync(id);
        request.Return();
        await _requests.UpdateAsync(request);
        await AddEventAsync(id, "Returned", input.Comment);
    }

    private async Task<PurchaseRequest> GetOwnedRequestAsync(Guid id)
    {
        var request = await _requests.GetAsync(id);
        if (request.BuyerUserId != UserId())
            throw new AbpAuthorizationException();
        return request;
    }

    private async Task EnsureUniqueOrderReferenceAsync(string orderReference, Guid? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(orderReference)) return;
        var normalized = orderReference.Trim();
        var query = await _requests.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(query.Where(x => x.OrderReference == normalized &&
                (!exceptId.HasValue || x.Id != exceptId.Value))))
            throw new BusinessException(PurchaseFlowErrors.DuplicateOrderReference);
    }

    private async Task<List<SupplierQuote>> GetQuotesAsync(Guid requestId)
    {
        var query = await _quotes.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(query.Where(x => x.PurchaseRequestId == requestId)
            .OrderBy(x => x.Amount));
    }

    private async Task AddEventAsync(Guid requestId, string action, string? comment)
    {
        await _events.InsertAsync(new ReviewEvent(GuidGenerator.Create(), CurrentTenant.Id,
            requestId, UserId(), action, comment));
    }

    private Guid UserId() => CurrentUser.Id ?? throw new AbpAuthorizationException();

    private static PurchaseRequestSummaryDto MapSummary(PurchaseRequest request) => new()
    {
        Id = request.Id,
        OrderReference = request.OrderReference,
        Description = request.Description,
        SaleAmount = request.SaleAmount,
        Status = request.Status,
        BuyerUserId = request.BuyerUserId,
        SelectedQuoteId = request.SelectedQuoteId,
        SelectionReason = request.SelectionReason,
        CreationTime = request.CreationTime
    };
}

