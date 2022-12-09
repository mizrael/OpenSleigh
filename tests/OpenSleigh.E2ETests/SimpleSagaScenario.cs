using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Messaging;

namespace OpenSleigh.E2ETests
{
    public abstract class SimpleSagaScenario : E2ETestsBase
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public async Task run_single_message_scenario(int hostsCount)
        {
            var message = new StartSimpleSaga();

            var receivedCount = 0;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

            Action<IMessageContext<StartSimpleSaga>> onMessage = ctx =>
            {
                receivedCount++;
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));

                ctx.Id.Should().NotBeNullOrWhiteSpace();
                ctx.SenderId.Should().NotBeNullOrWhiteSpace();
                ctx.ParentId.Should().BeNullOrWhiteSpace();
            };

            await RunScenarioAsync(hostsCount,
                (ctx, services) => services.AddSingleton(onMessage),
                async bus => await bus.PublishAsync(message, tokenSource.Token),
                tokenSource);

            receivedCount.Should().Be(1);
        }

        protected override void RegisterSagas(IBusConfigurator cfg)
        {
            cfg.AddSaga<SimpleSaga>();
        }
    }
}