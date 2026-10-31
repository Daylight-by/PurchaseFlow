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
    private readonly IPurchaseRequestRepository _requests;

    // Read-only projections of aggregate children. Every write goes through the
    // aggregate root; these repositories are only used to aggregate data for
    // list and dashboard queries.
    private readonly IRepository<SupplierQuote, Guid> _quotes;
    private readonly IRepository<PurchaseRequestLine, Guid> _lines;
    private readonly IRepository<PurchaseOrder, Guid> _orders;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLines;

    private readonly IRepository<ReviewEvent, Guid> _events;
    private readonly IdentityUserManager _userManager;

    public PurchaseRequestAppService(
        IPurchaseRequestRepository requests,
        IRepository<SupplierQuote, Guid> quotes,
        IRepository<PurchaseRequestLine, Guid> lines,
        IRepository<PurchaseOrder, Guid> orders,
        IRepository<PurchaseOrderLine, Guid> orderLines,
        IRepository<ReviewEvent, Guid> events,
        IdentityUserManager userManager)
    {
        _requests = requests;
        _quotes = quotes;
        _lines = lines;
        _orders = orders;
        _orderLines = orderLines;
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
        var totals = await GetLineTotalsAsync(requests.Select(x => x.Id));
        result.Items = requests.Select(request => MapSummary(request, totals)).ToList();
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
        var recentRequests = await AsyncExecuter.ToListAsync(requestsQuery
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id)
            .Take(5));
        var totals = await GetLineTotalsAsync(pendingReviews.Concat(recentRequests).Select(x => x.Id));
        result.PendingReviews = pendingReviews.Select(request => MapSummary(request, totals)).ToList();
        result.RecentRequests = recentRequests.Select(request => MapSummary(request, totals)).ToList();
        return result;
    }

    public async Task<PurchaseRequestDetailDto> GetAsync(Guid id)
    {
        var request = await GetRequestAsync(id);
        var eventsQuery = await _events.GetQueryableAsync();
        var events = await AsyncExecuter.ToListAsync(eventsQuery.Where(x => x.PurchaseRequestId == id)
            .OrderBy(x => x.OccurredAtUtc));
        var actorNames = new Dictionary<Guid, string>();
        foreach (var actorId in events.Select(x => x.ActorUserId).Distinct())
            actorNames[actorId] = (await _userManager.FindByIdAsync(actorId.ToString()))?.UserName
                ?? "已删除用户";

        var quotes = request.Quotes.OrderBy(x => x.Amount).ToList();
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
            CostPremiumToLowest = cheapest is null ? 0 : x.Amount - cheapest.Amount,
            // P0-1：把逐项报价明细一并返回，页面才能逐项展示“报价合计 = 明细合计”。
            // 集合按物料名排序输出：EF 加载集合的顺序本身不保证，固定顺序便于对照。
            Lines = x.Lines
                .OrderBy(line => line.ItemName)
                .Select(line => new SupplierQuoteLineDto
                {
                    Id = line.Id,
                    PurchaseRequestLineId = line.PurchaseRequestLineId,
                    ItemName = line.ItemName,
                    Specification = line.Specification,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Subtotal
                }).ToList()
        }).ToList();

        var totals = await GetLineTotalsAsync(new[] { request.Id });
        var summary = MapSummary(request, totals);
        return new PurchaseRequestDetailDto
        {
            Id = summary.Id,
            OrderReference = summary.OrderReference,
            Description = summary.Description,
            SaleAmount = summary.SaleAmount,
            Amount = summary.Amount,
            LineCount = summary.LineCount,
            Status = summary.Status,
            BuyerUserId = summary.BuyerUserId,
            SelectedQuoteId = summary.SelectedQuoteId,
            SelectionReason = summary.SelectionReason,
            CreationTime = summary.CreationTime,
            SelectedQuoteAmount = selected?.Amount,
            ExpectedGrossProfit = selected is null ? null : request.SaleAmount - selected.Amount,
            SelectedCostPremiumToLowest = selected is null || cheapest is null
                ? null : selected.Amount - cheapest.Amount,
            SelectedDeliveryDaysComparedWithLowest = selected is null || cheapest is null
                ? null : selected.DeliveryDays - cheapest.DeliveryDays,
            Lines = request.Lines
                .OrderBy(x => x.ItemName)
                .Select(x => new PurchaseRequestLineDto
                {
                    Id = x.Id,
                    ItemName = x.ItemName,
                    Specification = x.Specification,
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    Subtotal = x.Subtotal
                }).ToList(),
            Quotes = quoteDtos,
            PurchaseOrder = await GetPurchaseOrderAsync(request.Id),
            Decision = QuoteDecisionAnalyzer.Analyze(
                request.SaleAmount,
                request.SelectedQuoteId,
                request.SelectionReason,
                quoteDtos,
                request.Lines.Count,
                request.Amount),
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
        // P1-3：创建使用不含 Id 的 DTO，明细行 Id 全部由服务端生成。
        ApplyLinesForCreate(request, input.Lines);
        await _requests.InsertAsync(request);
        return request.Id;
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task UpdateAsync(Guid id, UpdatePurchaseRequestDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        await EnsureUniqueOrderReferenceAsync(input.OrderReference, id);
        request.SetDetails(input.OrderReference, input.Description, input.SaleAmount);
        // P1-3：更新使用带可空 Id 的 DTO，严格校验 Id 的归属后再更新/新增/删除明细。
        ApplyLinesForUpdate(request, input.Lines);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task<Guid> AddQuoteAsync(Guid id, AddSupplierQuoteDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        // P0-1：客户端只提交“需求明细 Id + 报价单价”，报价明细行 Id 由服务端生成，
        // 报价总额由领域按明细汇总 —— 接口不再接收手填总额。
        var quotedLines = input.Lines
            .Select(line => (Id: GuidGenerator.Create(), RequestLineId: line.RequestLineId, UnitPrice: line.UnitPrice))
            .ToList();
        var quote = request.AddQuote(GuidGenerator.Create(), input.SupplierName, input.DeliveryDays, input.Note, quotedLines);
        await _requests.UpdateAsync(request);
        return quote.Id;
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task DeleteQuoteAsync(Guid id, Guid quoteId)
    {
        var request = await GetOwnedRequestAsync(id);
        request.RemoveQuote(quoteId);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task SelectQuoteAsync(Guid id, SelectSupplierQuoteDto input)
    {
        var request = await GetOwnedRequestAsync(id);
        request.SelectQuote(input.QuoteId, input.Reason);
        await _requests.UpdateAsync(request);
    }

    [Authorize(PurchaseFlowPermissions.EditRequests)]
    public async Task SubmitAsync(Guid id)
    {
        var request = await GetOwnedRequestAsync(id);
        request.Submit();
        await _requests.UpdateAsync(request);
        await AddEventAsync(id, "Submitted", request.SelectionReason);
    }

    [Authorize(PurchaseFlowPermissions.ReviewRequests)]
    public async Task ApproveAsync(Guid id, ReviewPurchaseRequestDto input)
    {
        var request = await GetRequestAsync(id);
        request.Approve();
        await _requests.UpdateAsync(request);
        await AddEventAsync(id, "Approved", input.Comment);
        await CreatePurchaseOrderAsync(request);
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

    /// <summary>
    /// Approving a request turns the decision into a purchase order: the
    /// approved lines and the selected quote become an order for the supplier.
    /// </summary>
    private async Task CreatePurchaseOrderAsync(PurchaseRequest request)
    {
        var selectedQuote = request.Quotes.FirstOrDefault(x => x.Id == request.SelectedQuoteId);
        if (selectedQuote is null)
            return;

        if (await _orders.AnyAsync(x => x.PurchaseRequestId == request.Id))
            return;

        // P0-1：历史脏数据可能只有报价总额、没有逐项报价明细。若照旧从需求行
        // 预算单价生成订单，会再次出现“订单金额 ≠ 订单明细合计”的口径冲突。
        // 这里显式拒绝，避免生成空订单或来源不一致的订单。
        if (selectedQuote.Lines.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.QuoteLineMismatch);

        var order = new PurchaseOrder(
            GuidGenerator.Create(),
            request.TenantId,
            $"PO-{request.OrderReference}",
            request.Id,
            selectedQuote.SupplierName,
            selectedQuote.Amount,
            selectedQuote.DeliveryDays);

        // P0-1：订单明细取所选报价的逐项成交单价，不再使用需求行的预算单价；
        // AddLine 会按明细重算订单金额，使订单总额与订单明细合计严格一致。
        foreach (var line in selectedQuote.Lines)
            order.AddLine(GuidGenerator.Create(), line.ItemName, line.Specification, line.Quantity, line.UnitPrice);

        // 兜底断言：订单金额必须等于订单明细合计，不一致说明金额来源被绕过。
        if (order.Amount != order.Lines.Sum(x => x.Subtotal))
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);

        await _orders.InsertAsync(order);
    }

    private async Task<PurchaseOrderDto?> GetPurchaseOrderAsync(Guid requestId)
    {
        var order = await _orders.FirstOrDefaultAsync(x => x.PurchaseRequestId == requestId);
        if (order is null)
            return null;

        var linesQuery = await _orderLines.GetQueryableAsync();
        var lines = await AsyncExecuter.ToListAsync(linesQuery.Where(x => x.PurchaseOrderId == order.Id));
        return new PurchaseOrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            PurchaseRequestId = order.PurchaseRequestId,
            SupplierName = order.SupplierName,
            Amount = order.Amount,
            DeliveryDays = order.DeliveryDays,
            Status = order.Status,
            CreationTime = order.CreationTime,
            Lines = lines.Select(line => new PurchaseOrderLineDto
            {
                Id = line.Id,
                ItemName = line.ItemName,
                Specification = line.Specification,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Subtotal = line.Subtotal,
                ReceivedQuantity = line.ReceivedQuantity,
                RemainingQuantity = line.RemainingQuantity
            }).ToList()
        };
    }

    private async Task<PurchaseRequest> GetRequestAsync(Guid id)
    {
        // The request is loaded with its lines and its quotes, so the aggregate
        // can guard the rules that depend on them.
        var request = await _requests.FindWithDetailsAsync(id);
        return request ?? throw new EntityNotFoundException(typeof(PurchaseRequest), id);
    }

    private async Task<PurchaseRequest> GetOwnedRequestAsync(Guid id)
    {
        var request = await GetRequestAsync(id);
        if (request.BuyerUserId != UserId())
            throw new AbpAuthorizationException();
        return request;
    }

    /// <summary>
    /// P1-3：创建时明细一律生成新行 Id（创建 DTO 不含 Id 字段），从服务端入口
    /// 就避免“新建请求携带陌生 Id”的混用。
    /// </summary>
    private void ApplyLinesForCreate(PurchaseRequest request, List<CreatePurchaseRequestLineDto> lines)
    {
        var drafts = (lines ?? new List<CreatePurchaseRequestLineDto>())
            .Select(line => (
                Guid: GuidGenerator.Create(),
                ItemName: line.ItemName,
                Specification: line.Specification,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice))
            .ToList();
        request.SetLines(drafts);
    }

    /// <summary>
    /// P1-3：更新时严格区分三种情况：
    /// 1. 输入 Id 重复 → DuplicateLineId（无法判断要更新哪一行）；
    /// 2. 输入 Id 不属于当前需求 → LineNotInRequest（陌生 Id 必须拒绝，
    ///    不能静默降级成新增，否则调用方误传的 Id 会被悄悄忽略）；
    /// 3. 有 Id → 复用该 Id 就地更新；无 Id → 生成新 Id 新增。
    /// </summary>
    private void ApplyLinesForUpdate(PurchaseRequest request, List<UpdatePurchaseRequestLineDto> lines)
    {
        var input = lines ?? new List<UpdatePurchaseRequestLineDto>();

        var suppliedIds = input.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (suppliedIds.Count != suppliedIds.Distinct().Count())
            throw new BusinessException(PurchaseFlowErrors.DuplicateLineId);

        var existingIds = request.Lines.Select(x => x.Id).ToHashSet();
        if (suppliedIds.Any(lineId => !existingIds.Contains(lineId)))
            throw new BusinessException(PurchaseFlowErrors.LineNotInRequest);

        var drafts = input
            .Select(line => (
                Guid: line.Id ?? GuidGenerator.Create(),
                ItemName: line.ItemName,
                Specification: line.Specification,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice))
            .ToList();
        request.SetLines(drafts);
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

    private async Task<Dictionary<Guid, (decimal Amount, int Count)>> GetLineTotalsAsync(IEnumerable<Guid> requestIds)
    {
        var ids = requestIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, (decimal Amount, int Count)>();

        var linesQuery = await _lines.GetQueryableAsync();
        var lines = await AsyncExecuter.ToListAsync(linesQuery
            .Where(x => ids.Contains(x.PurchaseRequestId)));
        return lines
            .GroupBy(x => x.PurchaseRequestId)
            .ToDictionary(group => group.Key,
                group => (Amount: group.Sum(x => x.Subtotal), Count: group.Count()));
    }

    private async Task AddEventAsync(Guid requestId, string action, string? comment)
    {
        await _events.InsertAsync(new ReviewEvent(GuidGenerator.Create(), CurrentTenant.Id,
            requestId, UserId(), action, comment));
    }

    private Guid UserId() => CurrentUser.Id ?? throw new AbpAuthorizationException();

    private static PurchaseRequestSummaryDto MapSummary(
        PurchaseRequest request,
        IReadOnlyDictionary<Guid, (decimal Amount, int Count)> totals)
    {
        totals.TryGetValue(request.Id, out var total);
        return new PurchaseRequestSummaryDto
        {
            Id = request.Id,
            OrderReference = request.OrderReference,
            Description = request.Description,
            SaleAmount = request.SaleAmount,
            Amount = total.Amount,
            LineCount = total.Count,
            Status = request.Status,
            BuyerUserId = request.BuyerUserId,
            SelectedQuoteId = request.SelectedQuoteId,
            SelectionReason = request.SelectionReason,
            CreationTime = request.CreationTime
        };
    }
}
