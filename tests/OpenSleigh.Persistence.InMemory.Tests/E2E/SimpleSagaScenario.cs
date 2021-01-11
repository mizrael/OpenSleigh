using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory.Tests.E2E.Sagas;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.E2E
{
    public class SimpleSagaScenario
    {
        [Fact]
        public async Task SimpleSaga_should_handle_start_message()
        {
            var hostBuilder = CreateHostBuilder();

            var message = new StartSimpleSaga(Guid.NewGuid(), Guid.NewGuid());
            
            var received = false;
            Action<StartSimpleSaga> onMessage = msg =>
            {
                msg.Should().Be(message);
                received = true;
            };

            hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(onMessage);
            });

            var host = hostBuilder.Build();
            
            using var scope = host.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            await Task.WhenAll(new[]
            {
                host.RunAsync(token: tokenSource.Token),
                bus.PublishAsync(message, tokenSource.Token)
            });

            received.Should().BeTrue();
        }

        static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOpenSleigh(cfg =>
                    {
                        cfg.UseInMemoryTransport()
                            .UseInMemoryPersistence();

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                            .UseStateFactory(msg => new SimpleSagaState(msg.CorrelationId))
                            .UseInMemoryTransport();
                    });
                });
    }

}
