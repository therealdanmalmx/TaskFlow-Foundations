using System.Net;
using Microsoft.AspNetCore.Hosting;
using TaskFlow.Api.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Infrastructure.Health;

[Collection(TaskFlowApiCollection.Name)]
public class HealthApiTests(TaskFlowTestEnvironment environment, ITestOutputHelper output)
    : IntegrationTestBase(environment, output, authenticateAsAdmin: true)
{
    [Fact]
    public async Task Readiness_is_200_when_the_database_is_available()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_database_but_readiness_does()
    {
        using var factory = Application.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:Db",
                "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1");
            builder.UseSetting("RUN_MIGRATIONS", "false");
        });
        using var client = factory.CreateIntegrationClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}
