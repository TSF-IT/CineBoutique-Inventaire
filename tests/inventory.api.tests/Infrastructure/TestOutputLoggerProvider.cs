using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public TestOutputLoggerProvider(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestOutputLogger(_output, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class TestOutputLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _category;

        public TestOutputLogger(ITestOutputHelper output, string category)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _category = category ?? throw new ArgumentNullException(nameof(category));
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter is null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var message = formatter(state, exception);
            var exceptionSuffix = exception is null ? string.Empty : $" | EX: {exception}";
            _output.WriteLine($"[{logLevel}] {_category}: {message}{exceptionSuffix}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
