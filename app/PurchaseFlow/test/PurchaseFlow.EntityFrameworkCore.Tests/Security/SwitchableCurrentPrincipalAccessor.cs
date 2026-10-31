using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Volo.Abp.Security.Claims;

namespace PurchaseFlow.Security;

/// <summary>
/// A current principal accessor that lets a test act as different users.
/// The shared test base always returns the same administrator, which cannot
/// prove that a permission check really depends on the signed-in user.
/// It is registered explicitly by the authorization test module.
/// </summary>
public class SwitchableCurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private static readonly AsyncLocal<ClaimsPrincipal?> Ambient = new();

    public ClaimsPrincipal Principal => Ambient.Value ?? new ClaimsPrincipal(new ClaimsIdentity());

    public IDisposable Change(ClaimsPrincipal principal)
    {
        var previous = Ambient.Value;
        Ambient.Value = principal;
        return new RestoreScope(previous);
    }

    /// <summary>
    /// Acts as the given user until the returned scope is disposed. The roles
    /// are part of the principal because the ABP role permission value
    /// provider reads the role claims of the signed-in principal.
    /// </summary>
    public IDisposable SwitchTo(Guid userId, string userName, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userName),
            new Claim(AbpClaimTypes.Email, $"{userName}@example.invalid")
        };
        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));

        return Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "PurchaseFlowAuthorizationTest")));
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly ClaimsPrincipal? _previous;

        public RestoreScope(ClaimsPrincipal? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            Ambient.Value = _previous;
        }
    }
}
