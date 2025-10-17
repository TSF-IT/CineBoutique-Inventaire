using System.Text.Json.Serialization;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportGroupProposal(
    [property: JsonPropertyName("groupe")] string? Groupe,
    [property: JsonPropertyName("sousGroupe")] string? SousGroupe);
