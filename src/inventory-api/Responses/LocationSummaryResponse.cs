using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Responses;

public sealed record CountStatusItem(
    Guid? RunId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record LocationSummaryResponse(
    Guid LocationId,
    string LocationName,
    string? BusyBy,
    Guid? ActiveRunId,
    short? ActiveCountType,
    DateTimeOffset? ActiveStartedAtUtc,
    IReadOnlyList<CountStatusItem> CountStatuses);
