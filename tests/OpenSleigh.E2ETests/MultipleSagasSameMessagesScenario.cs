using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Messaging;

namespace OpenSleigh.E2ETests
{
    public abstract class MultipleSagasSameMessagesScenario : E2ETestsBase
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public async Task run_multiple_sagas_same_messages_scenario(int hostsCount)
        {
            var message = new StartMultipleSagas();

            var receivedCount = 0;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

            Action<IMessageContext<SagaCompleted>> onMessage = ctx =>
            {
                receivedCount++;
                if (receivedCount >= hostsCount)
                    tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            };

            await RunScenarioAsync(hostsCount,
                (ctx, services) => services.AddSingleton(onMessage),
                async bus => await bus.PublishAsync(message, tokenSource.Token),
                tokenSource);

            receivedCount.Should().Be(2);
        }

        protected override void RegisterSagas(IBusConfigurator cfg)
        {
            cfg.AddSaga<SagaWithoutState>();
            cfg.AddSaga<SagaWithState, MySagaState>();
        }
    }
}