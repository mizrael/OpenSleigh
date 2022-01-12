using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.MongoRabbit
{
    public class MongoRabbitEventBroadcastingScenario : EventBroadcastingScenario,
        IClassFixture<RabbitFixture>,
        IClassFixture<Persistence.Mongo.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly RabbitFixture _rabbitFixture;
        private readonly Persistence.Mongo.Tests.Fixtures.DbFixture _mongoFixture;
        private readonly string _exchangeName;
        private readonly List<string> _dbNames = new();
        
        public MongoRabbitEventBroadcastingScenario(RabbitFixture rabbitFixture, Persistence.Mongo.Tests.Fixtures.DbFixture mongoFixture)
        {
            _rabbitFixture = rabbitFixture;
            _mongoFixture = mongoFixture;
            _exchangeName = $"test.{nameof(DummyEvent)}.{Guid.NewGuid()}";
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, name) = _mongoFixture.CreateDbContext();
            _dbNames.Add(name);
            var mongoCfg = new MongoConfiguration(_mongoFixture.ConnectionString,
                name,
                MongoSagaStateRepositoryOptions.Default,
                MongoOutboxRepositoryOptions.Default);

            cfg.UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration, builder =>
                {
                    builder.UseMessageNamingPolicy<DummyEvent>(() =>
                    {
                        var sp = cfg.Services.BuildServiceProvider();
                        var sysInfo = sp.GetService<ISystemInfo>();
                        return new QueueReferences(_exchangeName,
                            $"{_exchangeName}.{sysInfo.ClientGroup}",
                            _exchangeName,
                            $"{_exchangeName}.dead",
                            $"{_exchangeName}.dead");
                    });
                })
               .UseMongoPersistence(mongoCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseRabbitMQTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var connectionFactory = new ConnectionFactory()
            {
                HostName = _rabbitFixture.RabbitConfiguration.HostName,
                UserName = _rabbitFixture.RabbitConfiguration.UserName,
                Password = _rabbitFixture.RabbitConfiguration.Password,
                VirtualHost = _rabbitFixture.RabbitConfiguration.VirtualHost,
                Port = AmqpTcpEndpoint.UseDefaultPort,
                DispatchConsumersAsync = true
            };
            using var connection = connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();
            channel.QueueDelete(_exchangeName); 
            channel.ExchangeDelete(_exchangeName);
            channel.QueueDelete($"{_exchangeName}.dead");
            channel.ExchangeDelete($"{_exchangeName}.dead");
        }
    }
}