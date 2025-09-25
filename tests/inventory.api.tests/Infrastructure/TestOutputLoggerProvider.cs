using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure
{
    public sealed class TestOutputLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;
        private IExternalScopeProvider? _scopeProvider;

        public TestOutputLoggerProvider(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public ILogger CreateLogger(string categoryName) =>
            new TestOutputLogger(_output, categoryName, _scopeProvider);

        public void Dispose() { }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        private sealed class TestOutputLogger : ILogger
        {
            private readonly ITestOutputHelper _output;
            private readonly string _categoryName;
            private readonly IExternalScopeProvider? _scopeProvider;

            public TestOutputLogger(ITestOutputHelper output, string categoryName, IExternalScopeProvider? scopeProvider)
            {
                _output = output;
                _categoryName = categoryName;
                _scopeProvider = scopeProvider;
            }

            // Pas de contrainte where TState : notnull ici
            public IDisposable BeginScope<TState>(TState state)
            {
                return _scopeProvider?.Push(state!) ?? NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            // Pas de contrainte where TState : notnull ici non plus
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                if (formatter is null) throw new ArgumentNullException(nameof(formatter));

                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message) && exception is null) return;

                var line = $"[{_categoryName}] {logLevel}: {message}";
                try
                {
                    _output.WriteLine(line);
                    if (exception is not null) _output.WriteLine(exception.ToString());
                }
                catch
                {
                    // xUnit peut râler si la sortie est utilisée après la fin du test
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
