using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    internal class FakeLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel, string, Exception)> _logs = new();
        
        public void Log<TState>(LogLevel logLevel, 
            EventId eventId, 
            TState state, 
            Exception exception, 
            Func<TState, Exception, string> formatter)
        {
            _logs.Add((logLevel, state.ToString(), exception));
        }

        public IReadOnlyCollection<(LogLevel level, string message, Exception ex)> Logs => _logs;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }
    }
}