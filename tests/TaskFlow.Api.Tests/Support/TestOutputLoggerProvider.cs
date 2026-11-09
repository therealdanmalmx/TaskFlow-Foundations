using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);

    public void Dispose()
    {
    }
}
