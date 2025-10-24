using System.Text.RegularExpressions;

namespace CineBoutique.Inventory.Api.Services.Products;

public static class ProductCodeValidator
{
    public const int MinLength = 5;
    public const int MaxLength = 20;
    public const string ValidationMessage =
        "Le code EAN/RFID doit contenir entre 5 et 20 caractères (lettres, chiffres, espaces, _, #, °, ', .).";

    private static readonly Regex AllowedCharactersRegex = new(
        @"^[\p{L}\p{Nd} _#°'.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? value, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            normalized = trimmed;
            return false;
        }

        if (!AllowedCharactersRegex.IsMatch(trimmed))
        {
            normalized = trimmed;
            return false;
        }

        normalized = trimmed;
        return true;
    }
}
