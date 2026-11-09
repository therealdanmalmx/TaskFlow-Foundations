using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public abstract class IntegrationTestBase(
    TaskFlowTestEnvironment environment,
    ITestOutputHelper output,
    bool useRedis = false,
    bool authenticateAsAdmin = false,
    bool startFromEmptyDatabase = false) : IAsyncLifetime
{
    private TestApplication? _testApplication;

    protected TaskFlowTestEnvironment Environment { get; } = environment;
    protected ITestOutputHelper Output { get; } = output;
    protected HttpClient Client { get; private set; } = null!;
    protected TaskFlowApplicationFactory Application => _testApplication!.Factory;

    public async Task InitializeAsync()
    {
        _testApplication = await Environment.CreateApplicationAsync(
            useRedis,
            startFromEmptyDatabase,
            Output);

        try
        {
            Client = Application.CreateIntegrationClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            var live = await Client.GetAsync("/health/live");
            live.EnsureSuccessStatusCode();

            if (authenticateAsAdmin)
            {
                Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    await Application.CreateAdminTokenAsync());
            }
        }
        catch
        {
            var diagnostics = await Environment.CaptureContainerLogsAsync("application-startup-failure");
            Output.WriteLine($"TaskFlow Testcontainers diagnostics: {diagnostics}");
            await _testApplication.DisposeAsync();
            _testApplication = null;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_testApplication is not null)
        {
            await _testApplication.DisposeAsync();
        }
    }

    protected async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = Application.CreateIntegrationClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await Application.CreateAdminTokenAsync());
        return client;
    }
}
