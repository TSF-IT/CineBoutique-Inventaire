// Modifications : extraction des enregistrements/classes internes utilisés par Dapper pour les requêtes API.
using System;

namespace CineBoutique.Inventory.Api.Models;

internal sealed record CountingRunRow(Guid Id, Guid InventorySessionId, Guid LocationId, short CountType);

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

    public string? OperatorDisplayName { get; set; }

    public DateTime StartedAtUtc { get; set; }
}

internal sealed class ConflictSummaryRow
{
    public Guid ConflictId { get; set; }

    public Guid CountLineId { get; set; }

    public Guid CountingRunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class LocationMetadataRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}
