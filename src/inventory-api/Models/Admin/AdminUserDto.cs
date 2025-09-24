namespace CineBoutique.Inventory.Api.Models.Admin;

public sealed record AdminUserDto(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);
