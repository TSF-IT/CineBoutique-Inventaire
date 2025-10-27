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
