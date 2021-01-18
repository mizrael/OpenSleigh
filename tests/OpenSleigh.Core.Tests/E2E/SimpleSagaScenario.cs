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
    public abstract class SimpleSagaScenario : E2ETestsBase
    {
        [Fact]
        public async Task run_single_message_scenario()
        {
            var hostBuilder = CreateHostBuilder();

            var message = new StartSimpleSaga(Guid.NewGuid(), Guid.NewGuid());

            var received = false;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            Action<StartSimpleSaga> onMessage = msg =>
            {
                received = true;

                tokenSource.Cancel();

                msg.Should().Be(message);
            };

            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);
            });

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
            AddSaga<SimpleSaga, SimpleSagaState, StartSimpleSaga>(cfg, msg => new SimpleSagaState(msg.CorrelationId));
        }
    }

}
