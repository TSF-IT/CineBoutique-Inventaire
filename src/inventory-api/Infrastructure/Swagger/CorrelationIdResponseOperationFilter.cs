using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CineBoutique.Inventory.Api.Infrastructure.Swagger;

public sealed class CorrelationIdResponseOperationFilter : IOperationFilter
{
    private const string HeaderName = "X-Correlation-Id";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation?.Responses is null || operation.Responses.Count == 0)
            return;

        foreach (var response in operation.Responses.Values)
        {
            response.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
            if (!response.Headers.ContainsKey(HeaderName))
            {
                response.Headers[HeaderName] = new OpenApiHeader
                {
                    Description = "Identifiant de corrélation (Guid format \"N\") renvoyé avec chaque réponse.",
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Pattern = "^[0-9a-fA-F]{32}$"
                    }
                };
            }
        }
    }
}
