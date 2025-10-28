using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CineBoutique.Inventory.Api.Features.Inventory;

internal static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/shops/{shopId:guid}/reports/inventory/zones.csv", async (
            Guid shopId,
            IRunRepository runRepository,
            CancellationToken cancellationToken) =>
        {
            var summaries = await runRepository
                .GetFinalizedZoneSummariesAsync(shopId, cancellationToken)
                .ConfigureAwait(false);

            var csv = BuildZonesCsv(summaries);
            var fileName = FormattableString.Invariant($"inventaire-zones-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");

            return CreateCsvFile(csv, fileName);
        })
        .WithName("DownloadInventoryByZoneCsv")
        .WithTags("Reports")
        .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        app.MapGet("/api/shops/{shopId:guid}/reports/inventory/sku.csv", async (
            Guid shopId,
            IRunRepository runRepository,
            CancellationToken cancellationToken) =>
        {
            var summaries = await runRepository
                .GetFinalizedZoneSummariesAsync(shopId, cancellationToken)
                .ConfigureAwait(false);

            var csv = BuildSkuCsv(summaries);
            var fileName = FormattableString.Invariant($"inventaire-sku-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");

            return CreateCsvFile(csv, fileName);
        })
        .WithName("DownloadInventoryBySkuCsv")
        .WithTags("Reports")
        .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        return app;
    }

    private static IResult CreateCsvFile(string csv, string fileName)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var body = encoding.GetBytes(csv);
        var preamble = encoding.GetPreamble();
        var buffer = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, buffer, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, buffer, preamble.Length, body.Length);

        return Results.File(buffer, "text/csv; charset=utf-8", fileName);
    }

    private static string BuildZonesCsv(IReadOnlyList<FinalizedZoneSummaryModel> summaries)
    {
        var builder = new StringBuilder();

        if (summaries.Count == 0)
        {
            builder.AppendLine("Zone;—;Opérateur;—");
            builder.AppendLine("Clôturé le;—");
            builder.AppendLine("EAN/RFID;SKU/item;Description;Quantité");
            return builder.ToString();
        }

        foreach (var zone in summaries
                     .OrderBy(summary => summary.LocationCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(summary => summary.LocationLabel, StringComparer.OrdinalIgnoreCase))
        {
            var zoneLabel = BuildZoneLabel(zone);
            var operatorLabel = string.IsNullOrWhiteSpace(zone.OperatorDisplayName) ? "—" : zone.OperatorDisplayName!.Trim();
            var completedAt = zone.CompletedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            builder.Append(EscapeCsv("Zone")).Append(';')
                .Append(EscapeCsv(zoneLabel)).Append(';')
                .Append(EscapeCsv("Opérateur")).Append(';')
                .AppendLine(EscapeCsv(operatorLabel));

            builder.Append(EscapeCsv("Clôturé le")).Append(';')
                .Append(EscapeCsv(completedAt)).AppendLine();

            builder.AppendLine("EAN/RFID;SKU/item;Description;Quantité");

            if (zone.Items.Count == 0)
            {
                builder.AppendLine("—;—;—;0");
            }
            else
            {
                foreach (var item in zone.Items
                             .OrderBy(i => string.IsNullOrWhiteSpace(i.Ean) ? i.Sku : i.Ean, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var quantity = FormatQuantity(item.Quantity);
                    builder.Append(EscapeCsv(string.IsNullOrWhiteSpace(item.Ean) ? "—" : item.Ean))
                        .Append(';')
                        .Append(EscapeCsv(string.IsNullOrWhiteSpace(item.Sku) ? "—" : item.Sku))
                        .Append(';')
                        .Append(EscapeCsv(string.IsNullOrWhiteSpace(item.Name) ? "—" : item.Name))
                        .Append(';')
                        .AppendLine(quantity);
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSkuCsv(IReadOnlyList<FinalizedZoneSummaryModel> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SKU/item;Zone;Quantité validée");

        if (summaries.Count == 0)
        {
            return builder.ToString();
        }

        foreach (var zone in summaries
                     .OrderBy(summary => summary.LocationCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(summary => summary.LocationLabel, StringComparer.OrdinalIgnoreCase))
        {
            var zoneLabel = BuildZoneLabel(zone);
            if (zone.Items.Count == 0)
            {
                continue;
            }

            foreach (var item in zone.Items
                         .OrderBy(i => string.IsNullOrWhiteSpace(i.Sku) ? i.Ean : i.Sku, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                var identifier = ResolveSkuOrEan(item);
                var quantity = FormatQuantity(item.Quantity);
                builder.Append(EscapeCsv(identifier)).Append(';')
                    .Append(EscapeCsv(zoneLabel)).Append(';')
                    .AppendLine(quantity);
            }
        }

        return builder.ToString();
    }

    private static string BuildZoneLabel(FinalizedZoneSummaryModel zone)
    {
        var code = zone.LocationCode?.Trim();
        var label = zone.LocationLabel?.Trim();

        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(label))
        {
            return $"{code} – {label}";
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            return code!;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            return label!;
        }

        return zone.LocationId.ToString("D");
    }

    private static string ResolveSkuOrEan(FinalizedZoneItemModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.Sku))
        {
            return item.Sku.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Ean))
        {
            return item.Ean.Trim();
        }

        return "—";
    }

    private static string EscapeCsv(string? value)
    {
        var sanitized = value?.Trim() ?? string.Empty;
        if (sanitized.Length == 0)
        {
            return "—";
        }

        if (sanitized.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0)
        {
            var escaped = sanitized.Replace("\"", "\"\"", StringComparison.Ordinal);
            return $"\"{escaped}\"";
        }

        return sanitized;
    }

    private static string FormatQuantity(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.InvariantCulture);
}
