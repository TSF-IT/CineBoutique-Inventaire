namespace CineBoutique.Inventory.Api.Models
{
    public sealed record CountStatusDto(
        short CountType,
        string Status,
        Guid? RunId,
        string? OwnerDisplayName,
        Guid? OwnerUserId,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    public sealed record LocationDto(
        Guid Id,
        string Code,
        string Label,
        bool IsBusy,
        string? BusyBy,
        Guid? ActiveRunId,
        short? ActiveCountType,
        DateTimeOffset? ActiveStartedAtUtc,
        IReadOnlyList<CountStatusDto> CountStatuses);
}
