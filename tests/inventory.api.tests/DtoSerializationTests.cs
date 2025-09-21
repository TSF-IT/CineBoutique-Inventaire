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
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Code = "Z1",
            Label = "Zone 1",
            IsBusy = true,
            BusyBy = "charles",
            ActiveRunId = Guid.Parse("ffffffff-1111-2222-3333-444444444444"),
            ActiveCountType = 2,
            ActiveStartedAtUtc = DateTimeOffset.Parse("2025-01-01T10:00:00Z", CultureInfo.InvariantCulture)
        };

        var json = JsonSerializer.Serialize(dto, WebSerializerOptions);

        Assert.Contains("\"id\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"Z1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label\":\"Zone 1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isBusy\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"busyBy\":\"charles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"activeRunId\":\"ffffffff-1111-2222-3333-444444444444\"", json, StringComparison.Ordinal);
        Assert.Contains("\"activeCountType\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"activeStartedAtUtc\":\"2025-01-01T10:00:00+00:00\"", json, StringComparison.Ordinal);
    }
}
