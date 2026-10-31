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
    private readonly IMaterialAppService _materialService;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    [BindProperty]
    public string OrderReference { get; set; } = string.Empty;

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public decimal SaleAmount { get; set; }

    [BindProperty]
    public List<LineInput> Lines { get; set; } = new();

    /// <summary>
    /// 第二轮第 1 批：「物料档案」下拉的可选项（只取启用中的档案）。
    /// 明细行仍可用空选项手工录入，保证旧流程与旧数据不受影响。
    /// </summary>
    public List<MaterialDto> Materials { get; private set; } = new();

    public string? Error { get; private set; }

    public CreateModel(IPurchaseRequestAppService service, IMaterialAppService materialService,
        IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _materialService = materialService;
        _localizer = localizer;
    }

    public async Task OnGetAsync()
    {
        await LoadMaterialsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // 校验失败要回到页面时，下拉选项仍然要存在，否则用户无法重新选择档案。
        await LoadMaterialsAsync();

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
                    UnitPrice = line.UnitPrice,
                    // 第二轮第 1 批：把可选的物料档案 Id 传给应用层，
                    // 由服务端校验档案存在/启用并用档案快照覆盖文本。
                    MaterialId = line.MaterialId
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

    /// <summary>
    /// 第二轮第 1 批：加载启用中的物料档案作为行编辑器下拉选项。
    /// 只取启用档案（停用档案不能用于新明细）；旧数据的手工录入不受影响。
    /// </summary>
    private async Task LoadMaterialsAsync()
    {
        var result = await _materialService.GetListAsync(new MaterialListInput
        {
            IsActive = true,
            Page = 1,
            // 与服务端 PageSize 上限 200 对齐，避免请求被截断。
            PageSize = 200
        });
        Materials = result.Items;
    }
}
