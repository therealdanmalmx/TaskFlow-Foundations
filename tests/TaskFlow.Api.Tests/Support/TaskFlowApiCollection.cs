using Xunit;

namespace TaskFlow.Api.Tests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TaskFlowApiCollection : ICollectionFixture<TaskFlowTestEnvironment>
{
    public const string Name = "TaskFlow API integration tests";
}
