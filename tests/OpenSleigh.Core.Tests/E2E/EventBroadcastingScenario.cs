using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            
            var expectedHosts = Enumerable.Range(1, hostsCount)
                .Select(i => $"host_{i}")
                .ToDictionary(h => h, h => 2);

            Action<IMessageContext<DummyEvent>> onMessage = async ctx =>
            {
                if (expectedHosts.ContainsKey(ctx.SystemInfo.ClientGroup))
                {
                    expectedHosts[ctx.SystemInfo.ClientGroup]--;
                    if (expectedHosts[ctx.SystemInfo.ClientGroup] < 1)
                        expectedHosts.Remove(ctx.SystemInfo.ClientGroup);
                }

                if (!expectedHosts.Any())
                    tokenSource.Cancel();
            };

            var createHostTasks = Enumerable.Range(1, hostsCount)
                           .Select(async i =>
                           {
                               var host = await SetupHostAsync(onMessage, i);
                               await host.StartAsync(tokenSource.Token);
                               return host;
                           }).ToArray();
            
            await Task.WhenAll(createHostTasks);
            
            var producerHost = createHostTasks.First().Result;
            using var scope = producerHost.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(message, tokenSource.Token);
            
            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(100);

            foreach (var t in createHostTasks)
            {
                try
                {
                    t.Result.Dispose();
                }
                catch{}
            }

            expectedHosts.Should().BeEmpty();
        }

        private async Task<IHost> SetupHostAsync(Action<IMessageContext<DummyEvent>> onMessage, int hostId)
        {
            var hostBuilder = CreateHostBuilder();
            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);

                var sysInfo = new SystemInfo(Guid.NewGuid(), $"host_{hostId}");
                services.Replace(ServiceDescriptor.Singleton(sysInfo));
            });
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
