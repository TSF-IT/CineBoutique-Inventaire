using System;
using System.Net.Http;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

internal static class HttpClientTestExtensions
{
    public static Uri CreateRelativeUri(this HttpClient client, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var baseAddress = client.BaseAddress ?? new Uri("http://localhost");
        var formattedPath = relativePath.Length > 0 && relativePath[0] == '/'
            ? relativePath
            : "/" + relativePath;

        return new Uri(baseAddress, formattedPath);
    }
}
