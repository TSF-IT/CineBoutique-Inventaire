using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

public sealed class StartRunParameters
{
    public Guid LocationId { get; set; }

    public Guid ShopId { get; set; }

    public Guid OwnerUserId { get; set; }

    public short CountType { get; set; }
}

public enum StartRunStatus
{
    Success,
    LocationNotFound,
    LocationDisabled,
    OwnerInvalid,
    SequentialPrerequisiteMissing,
    ConflictOtherOwner
}

public sealed class StartRunResult
{
    public StartRunStatus Status { get; set; }

    public StartRunInfo? Run { get; set; }

    public Guid LocationId { get; set; }

    public Guid ShopId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string? ConflictingOwnerLabel { get; set; }
}

public sealed class StartRunInfo
{
    public Guid RunId { get; set; }

    public Guid InventorySessionId { get; set; }

    public Guid LocationId { get; set; }

    public short CountType { get; set; }

    public Guid OwnerUserId { get; set; }

    public string? OwnerDisplayName { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public bool WasExistingRun { get; set; }
}

public sealed class SanitizedCountLineModel
{
    public string Ean { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public bool IsManual { get; set; }
}

public sealed class CompleteRunParameters
{
    public Guid LocationId { get; set; }

    public Guid OwnerUserId { get; set; }

    public short CountType { get; set; }

    public Guid? RunId { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public IReadOnlyList<SanitizedCountLineModel> Items { get; set; } = Array.Empty<SanitizedCountLineModel>();
}

public sealed class CompleteRunInfo
{
    public Guid RunId { get; set; }

    public Guid InventorySessionId { get; set; }

    public Guid LocationId { get; set; }

    public Guid ShopId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public int ItemsCount { get; set; }

    public decimal TotalQuantity { get; set; }

    public string? OwnerDisplayName { get; set; }
}

public sealed class CompleteRunResult
{
    public RepositoryError? Error { get; set; }

    public CompleteRunInfo? Run { get; set; }
}

public sealed class ReleaseRunParameters
{
    public Guid LocationId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid RunId { get; set; }
}

public sealed class ReleaseRunResult
{
    public RepositoryError? Error { get; set; }
}

public sealed class RestartRunParameters
{
    public Guid LocationId { get; set; }

    public Guid OwnerUserId { get; set; }

    public short CountType { get; set; }

    public DateTimeOffset RestartedAtUtc { get; set; }
}

public sealed class RestartRunInfo
{
    public Guid LocationId { get; set; }

    public Guid ShopId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public DateTimeOffset RestartedAtUtc { get; set; }

    public int ClosedRuns { get; set; }
}

public sealed class RestartRunResult
{
    public RepositoryError? Error { get; set; }

    public RestartRunInfo? Run { get; set; }
}

public sealed class ResetShopInventoryResult
{
    public Guid ShopId { get; set; }

    public string? ShopName { get; set; }

    public int RunsRemoved { get; set; }

    public int CountLinesRemoved { get; set; }

    public int ConflictsRemoved { get; set; }

    public int SessionsRemoved { get; set; }

    public int LocationsAffected { get; set; }
}

public sealed class SessionConflictObservation
{
    public Guid RunId { get; set; }

    public Guid CountLineId { get; set; }

    public short CountType { get; set; }

    public Guid? CountedByUserId { get; set; }

    public string? CountedByDisplayName { get; set; }

    public DateTimeOffset CountedAtUtc { get; set; }

    public int Quantity { get; set; }
}

public sealed class SessionConflictItem
{
    public Guid ProductId { get; set; }

    public string ProductRef { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<SessionConflictObservation> Observations { get; set; } = Array.Empty<SessionConflictObservation>();

    public double? SampleVariance { get; set; }

    public int? ResolvedQuantity { get; set; }
}

public sealed class SessionResolvedConflictItem
{
    public Guid ProductId { get; set; }

    public string ProductRef { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int ResolvedQuantity { get; set; }

    public DateTimeOffset ResolvedAtUtc { get; set; }

    public string ResolutionRule { get; set; } = string.Empty;
}

public sealed class SessionConflictResolutionResult
{
    public Guid SessionId { get; set; }

    public bool SessionExists { get; set; }

    public IReadOnlyList<SessionConflictItem> Conflicts { get; set; } = Array.Empty<SessionConflictItem>();

    public IReadOnlyList<SessionResolvedConflictItem> Resolved { get; set; } = Array.Empty<SessionResolvedConflictItem>();
}
