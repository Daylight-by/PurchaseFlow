using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PurchaseFlow.Localization;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Web.Models;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.Purchasing;

[Authorize(PurchaseFlowPermissions.EditRequests)]
public class CreateModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    [BindProperty]
    public string OrderReference { get; set; } = string.Empty;

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public decimal SaleAmount { get; set; }

    [BindProperty]
    public List<LineInput> Lines { get; set; } = new();

    public string? Error { get; private set; }

    public CreateModel(IPurchaseRequestAppService service, IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _localizer = localizer;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var lines = Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemName))
            .ToList();

        if (lines.Count == 0)
        {
            Error = _localizer[PurchaseFlowErrors.NeedPurchaseLines];
            return Page();
        }

        try
        {
            var id = await _service.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = OrderReference ?? string.Empty,
                Description = Description ?? string.Empty,
                SaleAmount = SaleAmount,
                // P1-3：创建请求改用独立的 CreatePurchaseRequestLineDto（类型上没有 Id），
                // 从类型上杜绝“新建请求夹带陌生 Id”的混用；页面上隐藏 Id 为空自然被忽略。
                Lines = lines.Select(line => new CreatePurchaseRequestLineDto
                {
                    ItemName = line.ItemName,
                    Specification = line.Specification,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                }).ToList()
            });
            return RedirectToPage("/Purchasing/Details", new { id });
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
            return Page();
        }
        catch (AbpValidationException)
        {
            Error = "请检查销售单号、需求说明、销售金额，以及每条明细的数量与单价。";
            return Page();
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }
    }
}
