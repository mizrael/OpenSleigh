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
    public abstract class ParentChildScenario : E2ETestsBase
    {
        [Fact]
        public async Task run_parent_child_scenario()
        {
            var hostBuilder = CreateHostBuilder();

            var message = new StartParentSaga(Guid.NewGuid(), Guid.NewGuid());

            var received = false;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            Action<ParentSagaCompleted> onMessage = msg =>
            {
                received = true;
                tokenSource.Cancel();
            };

            hostBuilder.ConfigureServices((ctx, services) => { services.AddSingleton(onMessage); });

            var host = hostBuilder.Build();

            using var scope = host.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await Task.WhenAll(new[]
            {
                host.RunAsync(token: tokenSource.Token),
                bus.PublishAsync(message, tokenSource.Token)
            });

            received.Should().BeTrue();
        }

        protected override void AddSagas(IBusConfigurator cfg)
        {
            AddSaga<ParentSaga, ParentSagaState, StartParentSaga>(cfg, msg => new ParentSagaState(msg.CorrelationId));
            AddSaga<ChildSaga, ChildSagaState, StartChildSaga>(cfg, msg => new ChildSagaState(msg.CorrelationId));
        }
    }
}
