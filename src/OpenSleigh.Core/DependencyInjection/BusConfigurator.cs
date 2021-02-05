using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.ExceptionPolicies;

[assembly: InternalsVisibleTo("UnitTests")]
namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly ISagaTypeResolver _typeResolver;
        private readonly SystemInfo _systemInfo;
        
        public IServiceCollection Services { get; }

        public BusConfigurator(IServiceCollection services, ISagaTypeResolver typeResolver, SystemInfo systemInfo)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public IBusConfigurator SetPublishOnly(bool value = true)
        {
            _systemInfo.PublishOnly = value;
            return this;
        } 

        public ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
            where TD : SagaState
            where TS : Saga<TD>
        {
            var hasMessages = _typeResolver.Register<TS, TD>();

            if (hasMessages)
            {
                Services.AddTransient<TS>()
                        .AddTransient<ISagaPolicyFactory<TS, TD>, DefaultSagaPolicyFactory<TS, TD>>()
                        .AddTransient<ISagaFactory<TS, TD>, DefaultSagaFactory<TS, TD>>()
                        .AddTransient<ISagaStateService<TS, TD>, SagaStateService<TS, TD>>()
                        .AddTransient<ISagaRunner<TS, TD>, SagaRunner<TS, TD>>()
                        .AddTransient<ISagaStateFactory<TD>, DefaultSagaStateFactory<TD>>();
            }

            return new SagaConfigurator<TS, TD>(Services);
        }
    }
}