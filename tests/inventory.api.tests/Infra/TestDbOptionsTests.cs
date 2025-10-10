using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infra;

public sealed class TestDbOptionsTests
{
    private const string PrimaryVariableName = "TEST_DB_CONN";
    private const string SecondaryVariableName = "TEST_DB_CONNECTION";

    [Fact]
    public void UseExternalDb_IsFalse_WhenNoVariableIsDefined()
    {
        using var scope = new EnvironmentVariableScope(
            (PrimaryVariableName, null),
            (SecondaryVariableName, null));

        TestDbOptions.UseExternalDb.Should().BeFalse();
        TestDbOptions.ExternalConnectionString.Should().BeNull();
    }

    [Theory]
    [InlineData(PrimaryVariableName)]
    [InlineData(SecondaryVariableName)]
    public void ExternalConnectionString_ReadsFromSupportedVariables(string variableName)
    {
        using var scope = new EnvironmentVariableScope(
            (PrimaryVariableName, null),
            (SecondaryVariableName, null));

        scope.Set(variableName, "Host=postgres;Port=5432;Database=tests");

        TestDbOptions.ExternalConnectionString.Should()
            .Be("Host=postgres;Port=5432;Database=tests");
        TestDbOptions.UseExternalDb.Should().BeTrue();
    }

    [Fact]
    public void ExternalConnectionString_PrefersPrimaryVariable()
    {
        using var scope = new EnvironmentVariableScope(
            (PrimaryVariableName, "Host=primary;"),
            (SecondaryVariableName, "Host=secondary;"));

        TestDbOptions.ExternalConnectionString.Should().Be("Host=primary;");
    }
}
