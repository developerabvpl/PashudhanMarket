using System.Net;
using System.Net.Http.Json;

namespace UPBazaar.IntegrationTests.Infrastructure;

public static class HttpAssertions
{
    /// <summary>
    /// Reads a successful response body, and on any other status fails with the payload the
    /// API actually returned instead of a deserialisation error further down the test.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        this HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();

            throw new Xunit.Sdk.XunitException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} "
                + $"returned {(int)response.StatusCode} {response.StatusCode}, expected {(int)expected}. "
                + $"Body: {body}");
        }

        var value = await response.Content.ReadFromJsonAsync<T>();

        return value ?? throw new Xunit.Sdk.XunitException(
            $"{response.RequestMessage?.RequestUri} returned an empty body.");
    }
}
