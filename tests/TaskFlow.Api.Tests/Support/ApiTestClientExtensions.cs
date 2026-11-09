using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskFlow.Api.Tests.Support;

public static class ApiTestClientExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static HttpClient CreateIntegrationClient(
        this WebApplicationFactory<Program> factory,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = options is null
            ? factory.CreateClient()
            : factory.CreateClient(options);
        client.Timeout = RequestTimeout;
        return client;
    }

    public static async Task<HttpResponseMessage> SendAuthenticatedAsync(
        this HttpClient client,
        string token,
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, cancellationToken);
    }
}
