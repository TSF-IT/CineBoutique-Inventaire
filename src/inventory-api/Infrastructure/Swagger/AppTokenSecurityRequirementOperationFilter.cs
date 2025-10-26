using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CineBoutique.Inventory.Api.Infrastructure.Swagger;

internal static class SwaggerSecuritySchemeNames
{
    internal const string AppToken = "X-App-Token";
    internal const string Admin = "X-Admin";
}

internal sealed class AppTokenSecurityRequirementOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation == null || context == null)
        {
            return;
        }

        if (AllowsAnonymous(context))
        {
            return;
        }

        EnsureRequirement(operation, SwaggerSecuritySchemeNames.AppToken);

        if (RequiresAdmin(context))
        {
            EnsureRequirement(operation, SwaggerSecuritySchemeNames.Admin);
        }
    }

    private static bool AllowsAnonymous(OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor?.EndpointMetadata;
        if (endpointMetadata?.OfType<IAllowAnonymous>().Any() == true)
        {
            return true;
        }

        if (context.MethodInfo != null)
        {
            if (context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any())
            {
                return true;
            }

            if (context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresAdmin(OperationFilterContext context)
    {
        return GetAuthorizeData(context).Any(data =>
            string.Equals(data.Policy, "Admin", System.StringComparison.OrdinalIgnoreCase));
    }

    private static List<IAuthorizeData> GetAuthorizeData(OperationFilterContext context)
    {
        var items = new List<IAuthorizeData>();

        var endpointMetadata = context.ApiDescription.ActionDescriptor?.EndpointMetadata;
        if (endpointMetadata != null)
        {
            items.AddRange(endpointMetadata.OfType<IAuthorizeData>());
        }

        if (context.MethodInfo != null)
        {
            items.AddRange(context.MethodInfo.GetCustomAttributes(true).OfType<IAuthorizeData>());

            var declaringType = context.MethodInfo.DeclaringType;
            if (declaringType != null)
            {
                items.AddRange(declaringType.GetCustomAttributes(true).OfType<IAuthorizeData>());
            }
        }

        return items;
    }

    private static void EnsureRequirement(OpenApiOperation operation, string schemeId)
    {
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        var alreadyPresent = operation.Security.Any(existing =>
            existing.Keys.Any(key => string.Equals(key.Reference?.Id, schemeId, System.StringComparison.Ordinal)));

        if (!alreadyPresent)
        {
            operation.Security.Add(CreateRequirement(schemeId));
        }
    }

    private static OpenApiSecurityRequirement CreateRequirement(string schemeId)
    {
        return new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = schemeId
                    }
                },
                System.Array.Empty<string>()
            }
        };
    }
}
