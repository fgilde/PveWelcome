using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PveWelcome.Services;

/// Bridges the cookie-authenticated HttpContext user into Blazor's auth state.
public class HttpContextAuthStateProvider(IHttpContextAccessor accessor) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
