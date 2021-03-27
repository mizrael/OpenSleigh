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
            
            Action<ParentSagaCompleted> onMessage = msg =>
            {
                receivedCount++;
                tokenSource.CancelAfter(TimeSpan.FromSeconds(10));

                msg.CorrelationId.Should().Be(message.CorrelationId);
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
                await Task.Delay(10);

            receivedCount.Should().Be(1);
        }

        private async Task<IHost> SetupHost(Action<ParentSagaCompleted> onMessage)
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
