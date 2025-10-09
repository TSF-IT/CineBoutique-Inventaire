using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;

namespace CineBoutique.Inventory.Api.Tests.Helpers;

internal static class HttpAsserts
{
    public static async Task ShouldBeAsync(this HttpResponseMessage res, HttpStatusCode expected, string because = "")
    {
        if (res.StatusCode != expected)
        {
            var body = res.Content is null ? "<no content>" : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            res.StatusCode.Should().Be(expected, $"expected {expected} but got {(int)res.StatusCode} {res.StatusCode}. Body: {body}. {because}");
        }
    }
}
