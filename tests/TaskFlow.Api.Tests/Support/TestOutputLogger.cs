using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace TaskFlow.Api.Tests.Support;

public sealed class TestOutputLogger(
    ITestOutputHelper output,
    string categoryName) : ILogger
{
    private static readonly object WriteLock = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message}{Environment.NewLine}{exception}";
        }

        lock (WriteLock)
        {
            try
            {
                output.WriteLine($"[{logLevel}] {categoryName}: {message}");
            }
            catch (InvalidOperationException)
            {
                // A late background log can race xUnit completing the test.
            }
        }
    }
}
