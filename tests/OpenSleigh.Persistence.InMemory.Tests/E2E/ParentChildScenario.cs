using System;
using System.ComponentModel;
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
    [Category("E2E")]
    [Trait("Category", "E2E")]
    public class ParentChildScenario
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
                        cfg.UseInMemoryTransport()
                            .UseInMemoryPersistence();

                        cfg.AddSaga<ParentSaga, ParentSagaState>()
                            .UseStateFactory(msg => new ParentSagaState(msg.CorrelationId))
                            .UseInMemoryTransport();

                        cfg.AddSaga<ChildSaga, ChildSagaState>()
                            .UseStateFactory(msg => new ChildSagaState(msg.CorrelationId))
                            .UseInMemoryTransport();
                    });
                });
    }

}
