using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.Outbox;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly SystemInfo _systemInfo;
        private readonly SagaDescriptorsResolver _sagaDescriptorResolver;

        public BusConfigurator(
            IServiceCollection services, 
            SystemInfo systemInfo, 
            SagaDescriptorsResolver sagaDescriptorResolver)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _sagaDescriptorResolver = sagaDescriptorResolver;
        }

        public IBusConfigurator WithOutboxProcessorOptions(OutboxProcessorOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            this.Services.Replace(ServiceDescriptor.Singleton(options));

            return this;
        }

        public IBusConfigurator AddSaga<TS, TD>()
            where TS : class, ISaga<TD>
            where TD : class, new()
        {
            _sagaDescriptorResolver.Register<TS, TD>();

            this.Services.AddTransient<TD>(_ => default) // this will allow DI container validation at startup
                         .AddTransient<TS>();

            return this;
        }

        public IBusConfigurator AddSaga<TS>()
             where TS : class, ISaga
        {
            _sagaDescriptorResolver.Register<TS>();
            
            this.Services.AddTransient<TS>();

            return this;
        }

        public IServiceCollection Services { get; }
    }
}