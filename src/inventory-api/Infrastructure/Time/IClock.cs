namespace CineBoutique.Inventory.Api.Infrastructure.Time
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
