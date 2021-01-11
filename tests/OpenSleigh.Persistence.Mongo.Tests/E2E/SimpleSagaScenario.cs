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
using OpenSleigh.Persistence.Mongo.Messaging;
using OpenSleigh.Persistence.Mongo.Tests.E2E.Sagas;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.E2E
{
    [Category("E2E")]
    [Trait("Category", "E2E")]
    public class SimpleSagaScenario : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SimpleSagaScenario(DbFixture fixture)
        {
            _fixture = fixture;
        }
        
        [Fact]
        public async Task run_single_message_scenario_with_in_memory_persistence()
        {
            var hostBuilder = CreateHostBuilder(_fixture.ConnectionString, _fixture.DbName);

            var message = new StartSimpleSaga(Guid.NewGuid(), Guid.NewGuid());
            
            var received = false;
            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            
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

        static IHostBuilder CreateHostBuilder(string connectionString, string dbName) =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOpenSleigh(cfg =>
                    {
                        var mongoCfg = new MongoConfiguration(connectionString,
                            dbName,
                            MongoSagaStateRepositoryOptions.Default,
                            MongoOutboxRepositoryOptions.Default);
                        
                        cfg.UseInMemoryTransport()
                            .UseMongoPersistence(mongoCfg);

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                            .UseStateFactory(msg => new SimpleSagaState(msg.CorrelationId))
                            .UseInMemoryTransport();
                    });
                });
    }

}
