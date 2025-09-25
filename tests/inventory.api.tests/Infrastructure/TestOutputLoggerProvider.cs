using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure
{
    public sealed class TestOutputLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public TestOutputLoggerProvider(ITestOutputHelper output)
        {
            ArgumentNullException.ThrowIfNull(output);
            _output = output;
        }

        public ILogger CreateLogger(string categoryName) => new TestOutputLogger(_output, categoryName);

        public void Dispose() { }

        private sealed class TestOutputLogger : ILogger
        {
            private readonly ITestOutputHelper _output;
            private readonly string _category;

            public TestOutputLogger(ITestOutputHelper output, string category)
            {
                _output = output;
                _category = category;
            }

            // Implémentation explicite + contrainte notnull (corrige CS8633)
            IDisposable ILogger.BeginScope<TState>(TState state) where TState : notnull
                => NullScope.Instance;

            bool ILogger.IsEnabled(LogLevel logLevel) => true;

            void ILogger.Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var msg = formatter(state, exception);
                _output.WriteLine($"[{logLevel}] {_category}: {msg}{(exception is null ? "" : $" | EX: {exception}")}");
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
