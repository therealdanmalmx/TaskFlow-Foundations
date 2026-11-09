using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Routing;

[Collection(TaskFlowApiCollection.Name)]
public class ClientRoutingTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output)
{
    [Fact]
    public async Task Unknown_API_endpoints_return_JSON_404_while_client_routes_use_the_SPA()
    {
        var unknownApi = await Client.GetAsync("/api/not-a-real-endpoint");
        var unknownAuthMutation = await Client.PostAsJsonAsync("/auth/not-a-real-endpoint", new { });
        var authRoute = await Client.GetAsync("/auth");
        var clientRoute = await Client.GetAsync("/workspace/settings/members");

        Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);
        Assert.Equal("application/json", unknownApi.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, unknownAuthMutation.StatusCode);
        Assert.Equal("application/json", unknownAuthMutation.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, authRoute.StatusCode);
        Assert.Equal("text/html", authRoute.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, clientRoute.StatusCode);
        Assert.Equal("text/html", clientRoute.Content.Headers.ContentType?.MediaType);
    }
}
