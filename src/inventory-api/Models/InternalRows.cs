// Modifications : extraction des enregistrements/classes internes utilisés par Dapper pour les requêtes API.
using System;

namespace CineBoutique.Inventory.Api.Models;

internal sealed record ProductLookupRow(Guid Id, string Ean);

internal sealed class LocationCountStatusRow
{
    public Guid LocationId { get; set; }

    public short CountType { get; set; }

    public Guid? RunId { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? OperatorDisplayName { get; set; }
}

internal sealed class OpenRunSummaryRow
{
    public Guid RunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OwnerDisplayName { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateTime StartedAtUtc { get; set; }
}

internal sealed class CompletedRunSummaryRow
{
    public Guid RunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OwnerDisplayName { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CompletedAtUtc { get; set; }
}

internal sealed class CompletedRunDetailRow
{
    public Guid RunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CompletedAtUtc { get; set; }
}

internal sealed class CompletedRunLineRow
{
    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Ean { get; set; }

    public decimal Quantity { get; set; }
}

internal sealed class ConflictZoneSummaryRow
{
    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public int ConflictLines { get; set; }
}

internal sealed class ConflictZoneItemRow
{
    public Guid ProductId { get; set; }

    public string Ean { get; set; } = string.Empty;

    public int QtyC1 { get; set; }

    public int QtyC2 { get; set; }
}

internal sealed class LastRunLookupRow
{
    public short CountType { get; set; }

    public Guid RunId { get; set; }
}

internal sealed class LocationMetadataRow
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

internal sealed class ActiveCountingRunRow
{
    public Guid RunId { get; set; }

    public Guid InventorySessionId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public Guid? OwnerUserId { get; set; }

    public string? OperatorDisplayName { get; set; }
}
