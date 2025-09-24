namespace CineBoutique.Inventory.Domain.Admin;

public interface IAdminUserRepository
{
    Task<AdminUserSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken);

    Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AdminUser> CreateAsync(string email, string displayName, CancellationToken cancellationToken);

    Task<AdminUser?> UpdateAsync(Guid id, string email, string displayName, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
