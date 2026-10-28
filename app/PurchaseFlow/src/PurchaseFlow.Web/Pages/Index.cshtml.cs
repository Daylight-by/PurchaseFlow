using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;

namespace PurchaseFlow.Web.Pages;

public class IndexModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IAuthorizationService _authorization;
    public PurchaseDashboardDto? Dashboard { get; private set; }
    public bool IsSignedIn => User.Identity?.IsAuthenticated == true;
    public IndexModel(IPurchaseRequestAppService service, IAuthorizationService authorization)
    {
        _service = service;
        _authorization = authorization;
    }
    public async Task OnGetAsync()
    {
        if (IsSignedIn && (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.Requests)).Succeeded)
            Dashboard = await _service.GetDashboardAsync();
    }
}

