using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using Volo.Abp.Domain.Entities;

namespace PurchaseFlow.Web.Pages.Purchasing;

[Authorize(PurchaseFlowPermissions.Requests)]
public class BriefModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;

    public PurchaseRequestDetailDto PurchaseRequest { get; private set; } = new();

    public BriefModel(IPurchaseRequestAppService service)
    {
        _service = service;
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            PurchaseRequest = await _service.GetAsync(id);
            return Page();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }
}

