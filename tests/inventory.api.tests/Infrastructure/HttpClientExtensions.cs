using System;
using System.Net.Http;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public static class HttpClientExtensions
{
    public static void ClearAuth(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Authorization = null;

        if (client.DefaultRequestHeaders.Contains("Authorization"))
        {
            client.DefaultRequestHeaders.Remove("Authorization");
        }
    }
}
