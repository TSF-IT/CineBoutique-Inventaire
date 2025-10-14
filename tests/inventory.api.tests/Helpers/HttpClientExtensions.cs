using System;
using System.Net.Http;

namespace CineBoutique.Inventory.Api.Tests.Helpers
{
    internal static class HttpClientExtensions
    {
        public static Uri CreateRelativeUri(this HttpClient client, string relativePath)
        {
            ArgumentNullException.ThrowIfNull(client);

            if (client.BaseAddress == null)
                throw new InvalidOperationException("HttpClient.BaseAddress n'est pas défini.");

            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Chemin relatif vide.", nameof(relativePath));

            if (!relativePath.StartsWith("/"))
                relativePath = "/" + relativePath;

            return new Uri(client.BaseAddress, relativePath);
        }

    }
}
