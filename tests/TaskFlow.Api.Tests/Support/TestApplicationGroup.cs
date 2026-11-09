namespace TaskFlow.Api.Tests.Support;

public sealed class TestApplicationGroup(
    TaskFlowTestEnvironment environment,
    string databaseName,
    IReadOnlyList<TaskFlowApplicationFactory> factories) : IAsyncDisposable
{
    public IReadOnlyList<TaskFlowApplicationFactory> Factories { get; } = factories;

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        foreach (var factory in Factories)
        {
            try
            {
                await factory.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
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
            throw new AggregateException("Failed to dispose grouped TaskFlow test applications.", failures);
        }
    }
}
