using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductSearchItemDto(
    [property: Required, JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("code")] string? Code,
    [property: Required, JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("subGroup")] string? SubGroup);

