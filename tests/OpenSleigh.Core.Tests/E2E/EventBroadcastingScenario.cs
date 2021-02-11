using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.E2E
{
    public abstract class EventBroadcastingScenario : E2ETestsBase
    {
        [Fact]
        public async Task run_event_broadcasting_scenario()
        {
            var hostBuilder = CreateHostBuilder();

            var message = new DummyEvent(Guid.NewGuid(), Guid.NewGuid());
            
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var callsCount = 0;
            var expectedCount = 2;
            
            Action<DummyEvent> onMessage = msg =>
            {
                callsCount++;

                if(callsCount >= expectedCount)
                    tokenSource.Cancel();
            };

            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);
            });

            var host = hostBuilder.Build();

            await host.SetupInfrastructureAsync();
            
            using var scope = host.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await Task.WhenAll(new[]
            {
                host.RunAsync(token: tokenSource.Token),
                bus.PublishAsync(message, tokenSource.Token)
            });

            callsCount.Should().Be(expectedCount);
        }
        
        protected override void AddSagas(IBusConfigurator cfg)
        {
            AddSaga<EventSaga1, EventSagaState1, DummyEvent>(cfg, msg => new EventSagaState1(msg.CorrelationId));
            AddSaga<EventSaga2, EventSagaState2, DummyEvent>(cfg, msg => new EventSagaState2(msg.CorrelationId));
        }
    }

}
