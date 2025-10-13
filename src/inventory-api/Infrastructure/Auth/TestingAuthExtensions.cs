using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace CineBoutique.Inventory.Api.Infrastructure.Auth;

public static class TestingAuthExtensions
{
    public static IServiceCollection AddTestingAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
            return services;

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
            options.DefaultChallengeScheme    = TestAuthHandler.Scheme;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

        // Si tes endpoints utilisent des policies nommées, on les enregistre ici.
        services.AddAuthorization(options =>
        {
            // Exemples courants
            options.AddPolicy("OperatorsOnly", p => p.RequireRole(Roles.Operator, Roles.Admin));
            options.AddPolicy("AdminsOnly",    p => p.RequireRole(Roles.Admin));
            options.AddPolicy("ViewersOrBetter", p => p.RequireRole(Roles.Viewer, Roles.Operator, Roles.Admin));
        });

        return services;
    }
}
