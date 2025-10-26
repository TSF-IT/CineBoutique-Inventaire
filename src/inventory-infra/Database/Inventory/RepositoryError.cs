using System.Collections.Generic;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

public sealed class RepositoryError
{
    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public IReadOnlyDictionary<string, object?>? Metadata { get; set; }
}
