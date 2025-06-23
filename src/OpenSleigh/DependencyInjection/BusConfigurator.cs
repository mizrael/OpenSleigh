using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.Outbox;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.DependencyInjection;

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
        ArgumentNullException.ThrowIfNull(options);

        this.Services.Replace(ServiceDescriptor.Singleton(options));

        return this;
    }

    public IBusConfigurator AddSaga<TS, TD>()
        where TS : class, ISaga<TD>
        where TD : class, new()
    {
        _sagaDescriptorResolver.Register<TS, TD>();

        // this will allow DI container validation at startup
        this.Services.AddTransient<TD>(_ => default)
                     .AddTransient<ISagaInstance<TD>>(_ => default)
                     .AddTransient<TS>();

        return this;
    }

    public IBusConfigurator AddSaga<TS>()
         where TS : class, ISaga
    {
        _sagaDescriptorResolver.Register<TS>();

        this.Services.AddTransient<TS>()
                     .AddTransient<ISagaInstance>(_ => default);

        return this;
    }

    public IBusConfigurator SetPublishOnly(bool value = true)
    {
        _systemInfo.PublishOnly = value;
        return this;
    }

    public IServiceCollection Services { get; }
}