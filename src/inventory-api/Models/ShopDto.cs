using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ShopDto(Guid Id, string Name, string Kind);
