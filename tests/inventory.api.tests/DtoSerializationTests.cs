using System;
using System.Globalization;
using System.Text.Json;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Tests;

public sealed class DtoSerializationTests
{
    private static readonly JsonSerializerOptions WebSerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LocationListItemDtoSerializesWithExpectedPropertyNames()
    {
        var dto = new LocationListItemDto
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"),
            Code = "Z1",
            Label = "Zone 1",
            IsBusy = true,
            BusyBy = "charles",
            ActiveRunId = Guid.Parse("ffffffff-1111-4111-8111-444444444444"),
            ActiveCountType = 2,
            ActiveStartedAtUtc = DateTimeOffset.Parse("2025-01-01T10:00:00Z", CultureInfo.InvariantCulture),
            CountStatuses =
            [
                new LocationCountStatusDto
                {
                    CountType = 1,
                    Status = LocationCountStatus.Completed,
                    RunId = Guid.Parse("bbbbbbbb-2222-4222-8222-555555555555"),
                    OwnerDisplayName = "louise",
                    OwnerUserId = Guid.Parse("bbbbbbbb-9999-4999-8999-aaaaaaaaaaaa"),
                    StartedAtUtc = "2024-12-31T08:00:00+00:00",
                    CompletedAtUtc = "2024-12-31T09:00:00+00:00"
                },
                new LocationCountStatusDto
                {
                    CountType = 2,
                    Status = LocationCountStatus.InProgress,
                    RunId = Guid.Parse("cccccccc-3333-4333-8333-666666666666"),
                    OwnerDisplayName = null,
                    OwnerUserId = null,
                    StartedAtUtc = "2025-01-01T10:00:00+00:00",
                    CompletedAtUtc = string.Empty
                }
            ]
        };

        var json = JsonSerializer.Serialize(dto, WebSerializerOptions);

        Assert.Contains("\"id\":\"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"Z1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label\":\"Zone 1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isBusy\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"busyBy\":\"charles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"activeRunId\":\"ffffffff-1111-4111-8111-444444444444\"", json, StringComparison.Ordinal);
        Assert.Contains("\"activeCountType\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"activeStartedAtUtc\":\"2025-01-01T10:00:00+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"countStatuses\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"countType\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ownerDisplayName\":\"louise\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ownerUserId\":\"bbbbbbbb-9999-4999-8999-aaaaaaaaaaaa\"", json, StringComparison.Ordinal);
        Assert.Contains("\"completedAtUtc\":\"2024-12-31T09:00:00+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"in_progress\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ownerDisplayName\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"startedAtUtc\":\"2025-01-01T10:00:00+00:00\"", json, StringComparison.Ordinal);
    }
}
