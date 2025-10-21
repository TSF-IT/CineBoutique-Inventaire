using System;
using System.IO;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed record ProductImportCommand(Stream CsvStream, bool DryRun, string? Username, Guid ShopId);
