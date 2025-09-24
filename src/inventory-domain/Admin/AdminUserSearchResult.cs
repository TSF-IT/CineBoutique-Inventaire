namespace CineBoutique.Inventory.Domain.Admin;

public sealed record AdminUserSearchResult(IReadOnlyList<AdminUser> Items, int TotalCount, int Page, int PageSize);
