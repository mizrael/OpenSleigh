using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.E2E
{
    [Category("E2E")]
    [Trait("Category", "E2E")]
    public abstract class E2ETestsBase
    {
        protected IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOpenSleigh(cfg =>
                    {
                        ConfigureTransportAndPersistence(cfg);
                        
                        AddSagas(cfg);
                    });
                    
                    services.Replace(new ServiceDescriptor(typeof(OutboxProcessorOptions),
                                                           new OutboxProcessorOptions(TimeSpan.Zero)));
                });
        
        protected void AddSaga<TS, TD, TM>(IBusConfigurator cfg, Func<TM, TD> stateFactory) 
            where TD : SagaState 
            where TS : Saga<TD>
            where TM : IMessage
        {
            var sagaCfg = cfg.AddSaga<TS, TD>()
                .UseStateFactory(stateFactory);

            ConfigureSagaTransport(sagaCfg);
        }

        protected abstract void ConfigureTransportAndPersistence(IBusConfigurator cfg);
        protected abstract void AddSagas(IBusConfigurator cfg);

        protected abstract void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg)
            where TD : SagaState
            where TS : Saga<TD>;
    }
}