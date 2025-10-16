using System.Text.RegularExpressions;

namespace CineBoutique.Inventory.Infrastructure.Database;

public static class CodeDigitsSanitizer
{
    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Build(string? code)
    {
        var digitsOnly = DigitsOnlyRegex.Replace(code ?? string.Empty, string.Empty);
        return string.IsNullOrEmpty(digitsOnly) ? null : digitsOnly;
    }
}
