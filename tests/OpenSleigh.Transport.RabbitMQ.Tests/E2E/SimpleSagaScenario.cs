using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Transport.RabbitMQ.Tests.E2E.Sagas;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.E2E
{
    [Category("E2E")]
    [Trait("Category", "E2E")]
    public class SimpleSagaScenario
    {
        [Fact]
        public async Task run_single_message_scenario_with_in_memory_persistence()
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

        static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOpenSleigh(cfg =>
                    {
                        var rabbitSection = hostContext.Configuration.GetSection("Rabbit");
                        var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"],
                            rabbitSection["UserName"],
                            rabbitSection["Password"]);
                        
                        cfg.UseRabbitMQTransport(rabbitCfg)
                            .UseInMemoryPersistence();

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                            .UseStateFactory(msg => new SimpleSagaState(msg.CorrelationId))
                            .UseRabbitMQTransport();
                    });
                });
    }

}
