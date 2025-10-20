using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

// Ce handler remplace le comportement par défaut : quoi qu'il arrive,
// on continue le pipeline (donc .RequireAuthorization() ne bloque plus) en TEST.
public sealed class AllowAllAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        return next(context);
    }
}
