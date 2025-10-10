using System;

namespace CineBoutique.Inventory.Api.Tests.Infra;

public static class TestDbOptions
{
    private const string PrimaryVariableName = "TEST_DB_CONN";
    private const string SecondaryVariableName = "TEST_DB_CONNECTION";

    public static string? ExternalConnectionString =>
        ReadConnectionString(PrimaryVariableName) ?? ReadConnectionString(SecondaryVariableName);

    public static bool UseExternalDb =>
        !string.IsNullOrWhiteSpace(ExternalConnectionString);

    private static string? ReadConnectionString(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
