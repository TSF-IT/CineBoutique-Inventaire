namespace CineBoutique.Inventory.Api.Models.Admin;

public sealed record AdminUserListResponse(IReadOnlyList<AdminUserDto> Items, int TotalCount, int Page, int PageSize);
