using System.Text.RegularExpressions;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal static class ProductImportFieldNormalizer
{
    internal static readonly Regex DigitsOnlyRegex =
        new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
