using System.Reflection;
using CineBoutique.Inventory.Api.Endpoints;
using FluentAssertions;

namespace CineBoutique.Inventory.Api.Tests;

public class ProductEanCandidatesTests
{
    private static readonly MethodInfo BuildCandidateEanCodesMethod = typeof(ProductEndpoints)
        .GetMethod("BuildCandidateEanCodes", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Impossible de localiser BuildCandidateEanCodes sur ProductEndpoints.");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildCandidateEanCodes_ReturnsEmpty_ForNullOrWhitespace(string? input)
    {
        var result = InvokeBuildCandidateEanCodes(input);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildCandidateEanCodes_IncludesVariants_ForCodeWithLeadingZeros()
    {
        var result = InvokeBuildCandidateEanCodes("000123");

        result.Should().Contain(new[] { "000123", "123", "00000123", "0000000000123" });
        result.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("7")]
    public void BuildCandidateEanCodes_PadsToEightAndThirteen_ForShortCodes(string code)
    {
        var result = InvokeBuildCandidateEanCodes(code);

        result.Should().Contain(code);
        result.Should().Contain(code.PadLeft(8, '0'));
        result.Should().Contain(code.PadLeft(13, '0'));
        result.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("12345678")]   // longueur 8
    [InlineData("00123456789")] // longueur 11 avec zéros en tête
    [InlineData("123456789012")] // longueur 12
    public void BuildCandidateEanCodes_PadsToThirteen_ForMediumLengthCodes(string code)
    {
        var trimmed = code.Trim();
        var result = InvokeBuildCandidateEanCodes(code);

        result.Should().Contain(trimmed);
        result.Should().Contain(trimmed.PadLeft(13, '0'));
        result.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildCandidateEanCodes_KeepsThirteenDigitCode_AsIs()
    {
        const string code = "1234567890123";

        var result = InvokeBuildCandidateEanCodes(code);

        result.Should().Equal(new[] { code });
    }

    [Theory]
    [InlineData("ABC123")]
    [InlineData("SKU-123")]
    public void BuildCandidateEanCodes_DoesNotFilterNonNumericCodes(string code)
    {
        var trimmed = code.Trim();
        var result = InvokeBuildCandidateEanCodes(code);

        result.Should().Contain(trimmed);
        result.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("000123")]
    [InlineData("12345678")]
    [InlineData("123456789012")]
    [InlineData("1234567890123")]
    [InlineData("ABC123")]
    public void BuildCandidateEanCodes_AlwaysReturnsUniqueCandidates(string code)
    {
        var result = InvokeBuildCandidateEanCodes(code);

        result.Should().OnlyHaveUniqueItems();
    }

    private static string[] InvokeBuildCandidateEanCodes(string? code)
    {
        var value = (string[]?)BuildCandidateEanCodesMethod.Invoke(null, new object?[] { code! });
        return value ?? Array.Empty<string>();
    }
}
