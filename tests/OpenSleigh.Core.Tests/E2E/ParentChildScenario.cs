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
            
            Action<ParentSagaCompleted> onMessage = msg =>
            {
                receivedCount++;
                tokenSource.CancelAfter(TimeSpan.FromSeconds(10));

                msg.CorrelationId.Should().Be(message.CorrelationId);
            };

            var hosts = new IHost[hostsCount];
            for (var i = 0; i < hostsCount; i++)
                hosts[i] = await SetupHost(onMessage, i);

            using var scope = hosts[0].Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var tasks = hosts.Select(host => host.RunAsync(token: tokenSource.Token))
                .Union(new[]
                {
                    bus.PublishAsync(message, tokenSource.Token)
                });

            await Task.WhenAll(tasks);

            receivedCount.Should().Be(1);
        }

        private async Task<IHost> SetupHost(Action<ParentSagaCompleted> onMessage, int hostId)
        {
            var hostBuilder = CreateHostBuilder();
            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);
                services.AddSingleton(new HostInfo(hostId));
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
