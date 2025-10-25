using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Validation;

internal static class InventoryCodeValidator
{
    public const int MaxLength = 64;

    private static readonly HashSet<char> AllowedSymbols = new(new[]
    {
        '_',
        '-',
        '#',
        '°',
        '\'',
        '.',
    });

    public static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static bool TryValidate(string code, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errorMessage = "Le code ne peut pas être vide.";
            return false;
        }

        if (code.Length > MaxLength)
        {
            errorMessage = $"Le code \"{code}\" dépasse la longueur maximale autorisée de {MaxLength} caractères.";
            return false;
        }

        foreach (var ch in code)
        {
            if (char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (AllowedSymbols.Contains(ch))
            {
                continue;
            }

            errorMessage =
                $"Le code \"{code}\" contient des caractères non pris en charge. Autorisés : lettres, chiffres, espaces, _, -, #, °, ', .";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
