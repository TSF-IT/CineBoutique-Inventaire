using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ShopUserDto(
    Guid Id,
    Guid ShopId,
    string Login,
    string DisplayName,
    bool IsAdmin,
    bool Disabled);
