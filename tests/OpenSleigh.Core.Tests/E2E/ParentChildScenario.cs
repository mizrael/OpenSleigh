using System;
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
    public abstract class ParentChildScenario : E2ETestsBase
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public async Task run_parent_child_scenario(int hostsCount)
        {
            var message = new StartParentSaga(Guid.NewGuid(), Guid.NewGuid());

            var receivedCount = 0;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            
            Action<IMessageContext<ParentSagaCompleted>> onMessage = ctx =>
            {
                receivedCount++;
                tokenSource.CancelAfter(TimeSpan.FromSeconds(10));

                ctx.Message.CorrelationId.Should().Be(message.CorrelationId);
            };

            var createHostTasks = Enumerable.Range(1, hostsCount)
                .Select(async i =>
                {
                    var host = await SetupHost(onMessage);
                    await host.StartAsync(tokenSource.Token);
                    return host;
                }).ToArray();

            await Task.WhenAll(createHostTasks);

            if (tokenSource.IsCancellationRequested)
                throw new Exception("a timeout occurred during hosts initialization.");

            var producerHost = createHostTasks.First().Result;
            using var scope = producerHost.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(message, tokenSource.Token);
            
            while (!tokenSource.IsCancellationRequested || !createHostTasks.All(ht => ht.IsCompleted))
                await Task.Delay(10);
            
            foreach (var t in createHostTasks)
            {
                try
                {
                    t.Result.Dispose();
                }
                catch{}
            }

            receivedCount.Should().Be(1);
        }

        private async Task<IHost> SetupHost(Action<IMessageContext<ParentSagaCompleted>> onMessage)
        {
            var hostBuilder = CreateHostBuilder();
            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);                
            });
            var host = hostBuilder.Build();
            
            await host.SetupInfrastructureAsync();
            return host;
        }

        protected override void AddSagas(IBusConfigurator cfg)
        {
            AddSaga<ParentSaga, ParentSagaState, StartParentSaga>(cfg, msg => new ParentSagaState(msg.CorrelationId));
            AddSaga<ChildSaga, ChildSagaState, StartChildSaga>(cfg, msg => new ChildSagaState(msg.CorrelationId));
        }
    }
}
