using System;
using System.Collections.Generic;
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
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.MasterData;

/// <summary>
/// 第二轮第 1 批：供应商档案页面（列表 + 新建/编辑表单 + 启停用）。
/// 为什么单独建页面：报价以前只能手填供应商名称，名称拼写不一致、账期与评级无处维护；
/// 档案页让"选择供应商"成为可管理动作，同时采购单据仍保存名称快照，档案改名/停用不影响历史。
/// 写操作只在 ManageMasterData 权限下显示；查看权限 MasterData 即可。
/// </summary>
[Authorize(PurchaseFlowPermissions.MasterData)]
public class SuppliersModel : PurchaseFlowPageModel
{
    private const int PageSize = 50;

    private readonly ISupplierAppService _service;
    private readonly IAuthorizationService _authorization;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public SupplierListDto Result { get; private set; } = new();
    public SupplierListInput Filter { get; private set; } = new();
    public bool CanManage { get; private set; }
    public string? Error { get; private set; }

    /// <summary>编辑中的档案 Id；为空表示新建模式。?editId= 进入编辑回填。</summary>
    public Guid? EditId { get; private set; }
    public bool IsEditing => EditId.HasValue;
    public int TotalPages => Math.Max(1, (Result.TotalCount + Math.Max(1, Result.PageSize) - 1) / Math.Max(1, Result.PageSize));

    /// <summary>评级只做简单枚举展示（A 最优，C 默认），不做自动评级算法。</summary>
    public static IReadOnlyList<SupplierRating> Ratings { get; } = new[] { SupplierRating.A, SupplierRating.B, SupplierRating.C };

    /// <summary>新建/编辑表单回传的档案 Id，用来区分 Create 与 Update（不放在 URL 上，避免误编辑）。</summary>
    [BindProperty]
    public Guid? EditIdInput { get; set; }

    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? ContactName { get; set; }
    [BindProperty] public string? ContactPhone { get; set; }
    [BindProperty] public int PaymentTermsDays { get; set; }
    [BindProperty] public SupplierRating Rating { get; set; } = SupplierRating.C;
    [BindProperty] public string? Note { get; set; }

    public SuppliersModel(ISupplierAppService service, IAuthorizationService authorization,
        IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _authorization = authorization;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(string? keyword, bool? isActive, Guid? editId, int pageNumber = 1)
    {
        Filter = new SupplierListInput
        {
            Keyword = keyword,
            IsActive = isActive,
            Page = Math.Max(1, pageNumber),
            PageSize = PageSize
        };
        await LoadAsync();

        if (editId.HasValue)
        {
            try
            {
                var supplier = await _service.GetAsync(editId.Value);
                // 编辑模式：把档案回填到同一页面的表单，避免再多一个编辑页。
                EditId = supplier.Id;
                EditIdInput = supplier.Id;
                Code = supplier.Code;
                Name = supplier.Name;
                ContactName = supplier.ContactName;
                ContactPhone = supplier.ContactPhone;
                PaymentTermsDays = supplier.PaymentTermsDays;
                Rating = supplier.Rating;
                Note = supplier.Note;
            }
            catch (EntityNotFoundException)
            {
                Error = "要编辑的供应商档案不存在，已返回列表。";
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        try
        {
            if (EditIdInput.HasValue)
            {
                await _service.UpdateAsync(EditIdInput.Value, new UpdateSupplierDto
                {
                    Code = Code ?? string.Empty,
                    Name = Name ?? string.Empty,
                    ContactName = ContactName,
                    ContactPhone = ContactPhone,
                    PaymentTermsDays = PaymentTermsDays,
                    Rating = Rating,
                    Note = Note
                });
            }
            else
            {
                await _service.CreateAsync(new CreateSupplierDto
                {
                    Code = Code ?? string.Empty,
                    Name = Name ?? string.Empty,
                    ContactName = ContactName,
                    ContactPhone = ContactPhone,
                    PaymentTermsDays = PaymentTermsDays,
                    Rating = Rating,
                    Note = Note
                });
            }

            // 保存成功后回到列表（PRG 模式），避免刷新重复提交；筛选条件不保留，保持简单。
            return RedirectToPage("/MasterData/Suppliers");
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
        }
        catch (AbpValidationException)
        {
            Error = "请检查供应商编码、名称、账期与各字段长度。";
        }
        catch (EntityNotFoundException)
        {
            Error = "要保存的供应商档案不存在。";
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }

        // 失败时保留用户输入并重新加载列表与权限，页面直接显示错误。
        EditId = EditIdInput;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool isActive)
    {
        try
        {
            await _service.SetActiveAsync(id, isActive);
            return RedirectToPage("/MasterData/Suppliers");
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
        }
        catch (EntityNotFoundException)
        {
            Error = "供应商档案不存在。";
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }

        await LoadAsync();
        return Page();
    }

    public static string RatingText(SupplierRating rating) => rating switch
    {
        SupplierRating.A => "A（优先）",
        SupplierRating.B => "B（合格）",
        SupplierRating.C => "C（观察）",
        _ => rating.ToString()
    };

    private async Task LoadAsync()
    {
        Result = await _service.GetListAsync(Filter);
        CanManage = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ManageMasterData)).Succeeded;
    }
}
