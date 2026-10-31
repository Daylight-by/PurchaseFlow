using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PurchaseFlow.Localization;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.Purchasing;

[Authorize(PurchaseFlowPermissions.Requests)]
public class IndexModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IAuthorizationService _authorization;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public PurchaseRequestListDto Result { get; private set; } = new();
    public PurchaseRequestListInput Filter { get; private set; } = new();
    public bool CanEdit { get; private set; }
    public string? Error { get; private set; }
    public int TotalPages => Math.Max(1, (Result.TotalCount + Result.PageSize - 1) / Result.PageSize);

    public IndexModel(IPurchaseRequestAppService service, IAuthorizationService authorization,
        IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _authorization = authorization;
        _localizer = localizer;
    }

    public async Task OnGetAsync(string? keyword, PurchaseRequestStatus? status, int pageNumber = 1)
    {
        Filter = new PurchaseRequestListInput { Keyword = keyword, Status = status, Page = pageNumber };
        await LoadAsync();
    }

    public static string StatusText(PurchaseRequestStatus status) => status switch
    {
        PurchaseRequestStatus.Draft => "草稿",
        PurchaseRequestStatus.PendingReview => "待审核",
        PurchaseRequestStatus.Approved => "已批准",
        PurchaseRequestStatus.Returned => "已退回",
        _ => status.ToString()
    };

    private async Task LoadAsync()
    {
        Result = await _service.GetListAsync(Filter);
        CanEdit = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.EditRequests)).Succeeded;
    }
}


