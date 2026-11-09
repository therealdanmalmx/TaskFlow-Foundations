namespace TaskFlow.Api.Tests.Support;

public sealed class TestApplication(
    TaskFlowTestEnvironment environment,
    string databaseName,
    TaskFlowApplicationFactory factory) : IAsyncDisposable
{
    public TaskFlowApplicationFactory Factory { get; } = factory;

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        try
        {
            await Factory.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await environment.DropDatabaseAsync(databaseName);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Failed to dispose an isolated TaskFlow test application.", failures);
        }
    }
}
