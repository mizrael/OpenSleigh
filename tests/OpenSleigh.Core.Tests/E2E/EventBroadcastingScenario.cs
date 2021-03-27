using System;
using System.Collections.Generic;
using System.Linq;
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
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public async Task run_event_broadcasting_scenario(int hostsCount)
        {
            var message = new DummyEvent(Guid.NewGuid(), Guid.NewGuid());
            
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var callsCount = 0;
            var expectedCount = 2;

            Action<DummyEvent> onMessage = async msg =>
            {
                callsCount++;

                if (callsCount < expectedCount) 
                    return;

                tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            };

            var consumerHostsTasks = Enumerable.Range(1, hostsCount - 1)
                           .Select(async i =>
                           {
                               var host = await SetupHost(onMessage);
                               await host.StartAsync(tokenSource.Token);
                           });

            var producerHostTask = Task.Run(async () =>
            {
                var host = await SetupHost(onMessage);

                await host.StartAsync(tokenSource.Token);

                using var scope = host.Services.CreateScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(message, tokenSource.Token);
            });

            var tasks = new List<Task>(consumerHostsTasks)
            {
                producerHostTask
            };

            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(100);

            callsCount.Should().Be(expectedCount);
        }

        private async Task<IHost> SetupHost(Action<DummyEvent> onMessage)
        {
            var hostBuilder = CreateHostBuilder();
            hostBuilder.ConfigureServices((ctx, services) => { services.AddSingleton(onMessage); });
            var host = hostBuilder.Build();
            await host.SetupInfrastructureAsync();
            return host;
        }

        protected override void AddSagas(IBusConfigurator cfg)
        {
            AddSaga<EventSaga1, EventSagaState1, DummyEvent>(cfg, msg => new EventSagaState1(msg.CorrelationId));
            AddSaga<EventSaga2, EventSagaState2, DummyEvent>(cfg, msg => new EventSagaState2(msg.CorrelationId));
        }
    }

}
