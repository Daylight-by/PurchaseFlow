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
using Volo.Abp.Domain.Entities;
using Volo.Abp.Users;
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.Purchasing;

[Authorize(PurchaseFlowPermissions.Requests)]
public class DetailsModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentUser _currentUser;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public PurchaseRequestDetailDto PurchaseRequest { get; private set; } = new();
    public bool CanEdit { get; private set; }
    public bool CanReview { get; private set; }
    public string? Error { get; private set; }

    public DetailsModel(IPurchaseRequestAppService service, IAuthorizationService authorization,
        ICurrentUser currentUser, IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _authorization = authorization;
        _currentUser = currentUser;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            await LoadAsync(id);
            return Page();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    public Task<IActionResult> OnPostUpdateAsync(Guid id, string orderReference, string description, decimal saleAmount) =>
        RunAsync(id, () => _service.UpdateAsync(id, new UpdatePurchaseRequestDto
        {
            OrderReference = orderReference ?? string.Empty,
            Description = description ?? string.Empty,
            SaleAmount = saleAmount
        }));

    public Task<IActionResult> OnPostAddQuoteAsync(Guid id, string supplierName, decimal amount,
        int deliveryDays, string? note) => RunAsync(id, async () =>
        await _service.AddQuoteAsync(id, new AddSupplierQuoteDto
        {
            SupplierName = supplierName ?? string.Empty,
            Amount = amount,
            DeliveryDays = deliveryDays,
            Note = note
        }));

    public Task<IActionResult> OnPostDeleteQuoteAsync(Guid id, Guid quoteId) =>
        RunAsync(id, () => _service.DeleteQuoteAsync(id, quoteId));

    public Task<IActionResult> OnPostSelectAsync(Guid id, Guid quoteId, string reason) =>
        RunAsync(id, () => _service.SelectQuoteAsync(id, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = reason ?? string.Empty
        }));

    public Task<IActionResult> OnPostSubmitAsync(Guid id) => RunAsync(id, () => _service.SubmitAsync(id));

    public Task<IActionResult> OnPostApproveAsync(Guid id, string? comment) =>
        RunAsync(id, () => _service.ApproveAsync(id, new ReviewPurchaseRequestDto { Comment = comment }));

    public Task<IActionResult> OnPostReturnAsync(Guid id, string? comment) =>
        RunAsync(id, () => _service.ReturnAsync(id, new ReviewPurchaseRequestDto { Comment = comment }));

    public static string ActionText(string action) => action switch
    {
        "Submitted" => "提交审核",
        "Approved" => "批准",
        "Returned" => "退回",
        _ => action
    };

    private async Task<IActionResult> RunAsync(Guid id, Func<Task> action)
    {
        try
        {
            await action();
            return RedirectToPage("/Purchasing/Details", new { id });
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
            await LoadAsync(id);
            return Page();
        }
        catch (AbpValidationException)
        {
            Error = "请检查必填项、金额范围和输入长度。";
            await LoadAsync(id);
            return Page();
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task LoadAsync(Guid id)
    {
        PurchaseRequest = await _service.GetAsync(id);
        CanEdit = PurchaseRequest.BuyerUserId == _currentUser.Id &&
                  (PurchaseRequest.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned) &&
                  (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.EditRequests)).Succeeded;
        CanReview = PurchaseRequest.Status == PurchaseRequestStatus.PendingReview &&
                    (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ReviewRequests)).Succeeded;
    }
}


