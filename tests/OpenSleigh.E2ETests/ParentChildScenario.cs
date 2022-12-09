using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Messaging;

namespace OpenSleigh.E2ETests
{
    public abstract class ParentChildScenario : E2ETestsBase
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public async Task run_parent_child_scenario(int hostsCount)
        {
            var message = new StartParentSaga();

            var receivedCount = 0;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

            Action<IMessageContext<ParentSagaCompleted>> onMessage = ctx =>
            {
                receivedCount++;
                tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            };

            await RunScenarioAsync(hostsCount,
                (ctx, services) => services.AddSingleton(onMessage),
                async bus => await bus.PublishAsync(message, tokenSource.Token),
                tokenSource);

            receivedCount.Should().Be(1);
        }

        protected override void RegisterSagas(IBusConfigurator cfg)
        {
            cfg.AddSaga<ParentSaga>();
            cfg.AddSaga<ChildSaga>();
        }
    }
}