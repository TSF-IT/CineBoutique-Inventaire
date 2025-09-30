// Modifications : déplacement de l'utilitaire de conversion temporelle pour clarifier Program.cs.
using System;

namespace CineBoutique.Inventory.Api.Infrastructure;

internal static class TimeUtil
{
    public static DateTimeOffset ToUtcOffset(DateTime dt) =>
        new(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

    public static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? ToUtcOffset(dt.Value) : (DateTimeOffset?)null;
}
