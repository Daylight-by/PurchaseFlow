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
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.MasterData;

/// <summary>
/// 第二轮第 1 批：物料档案页面（列表 + 新建/编辑表单 + 启停用）。
/// 为什么建档案：采购明细以前只能手填物料名称/规格，同一物料多次录入容易不一致；
/// 档案提供稳定的编码、名称、规格与单位，明细关联后仍保存名称/规格快照，档案改名不影响历史单据。
/// 写操作只在 ManageMasterData 权限下显示；查看权限 MasterData 即可。
/// </summary>
[Authorize(PurchaseFlowPermissions.MasterData)]
public class MaterialsModel : PurchaseFlowPageModel
{
    private const int PageSize = 50;

    private readonly IMaterialAppService _service;
    private readonly IAuthorizationService _authorization;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public MaterialListDto Result { get; private set; } = new();
    public MaterialListInput Filter { get; private set; } = new();
    public bool CanManage { get; private set; }
    public string? Error { get; private set; }

    /// <summary>编辑中的档案 Id；为空表示新建模式。?editId= 进入编辑回填。</summary>
    public Guid? EditId { get; private set; }
    public bool IsEditing => EditId.HasValue;
    public int TotalPages => Math.Max(1, (Result.TotalCount + Math.Max(1, Result.PageSize) - 1) / Math.Max(1, Result.PageSize));

    /// <summary>新建/编辑表单回传的档案 Id，用来区分 Create 与 Update。</summary>
    [BindProperty]
    public Guid? EditIdInput { get; set; }

    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Specification { get; set; } = string.Empty;
    [BindProperty] public string Unit { get; set; } = string.Empty;
    [BindProperty] public string? Category { get; set; }
    [BindProperty] public string? Note { get; set; }

    public MaterialsModel(IMaterialAppService service, IAuthorizationService authorization,
        IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _authorization = authorization;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(string? keyword, bool? isActive, Guid? editId, int pageNumber = 1)
    {
        Filter = new MaterialListInput
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
                var material = await _service.GetAsync(editId.Value);
                // 编辑模式：把档案回填到同一页面的表单。
                EditId = material.Id;
                EditIdInput = material.Id;
                Code = material.Code;
                Name = material.Name;
                Specification = material.Specification;
                Unit = material.Unit;
                Category = material.Category;
                Note = material.Note;
            }
            catch (EntityNotFoundException)
            {
                Error = "要编辑的物料档案不存在，已返回列表。";
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
                await _service.UpdateAsync(EditIdInput.Value, new UpdateMaterialDto
                {
                    Code = Code ?? string.Empty,
                    Name = Name ?? string.Empty,
                    Specification = Specification ?? string.Empty,
                    Unit = Unit ?? string.Empty,
                    Category = Category,
                    Note = Note
                });
            }
            else
            {
                await _service.CreateAsync(new CreateMaterialDto
                {
                    Code = Code ?? string.Empty,
                    Name = Name ?? string.Empty,
                    Specification = Specification ?? string.Empty,
                    Unit = Unit ?? string.Empty,
                    Category = Category,
                    Note = Note
                });
            }

            // 保存成功后回到列表（PRG 模式），避免刷新重复提交。
            return RedirectToPage("/MasterData/Materials");
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
        }
        catch (AbpValidationException)
        {
            Error = "请检查物料编码、名称、规格、单位与各字段长度。";
        }
        catch (EntityNotFoundException)
        {
            Error = "要保存的物料档案不存在。";
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
            return RedirectToPage("/MasterData/Materials");
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
        }
        catch (EntityNotFoundException)
        {
            Error = "物料档案不存在。";
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Result = await _service.GetListAsync(Filter);
        CanManage = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ManageMasterData)).Succeeded;
    }
}
