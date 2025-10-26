using System.Text.Json;
using System.Text.Json.Serialization;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal static class ProductImportSerialization
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
