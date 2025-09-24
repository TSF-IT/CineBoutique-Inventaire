namespace CineBoutique.Inventory.Domain.Admin;

public sealed class AdminUser
{
    public AdminUser(Guid id, string email, string displayName, DateTimeOffset createdAtUtc, DateTimeOffset? updatedAtUtc)
    {
        Id = id;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public string Email { get; }

    public string DisplayName { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? UpdatedAtUtc { get; }
}
