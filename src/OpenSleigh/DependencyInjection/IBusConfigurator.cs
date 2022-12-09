using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;

namespace OpenSleigh.DependencyInjection
{
    public interface IBusConfigurator 
    {
        IBusConfigurator AddSaga<TS>()
            where TS : class, ISaga;

        IBusConfigurator AddSaga<TS, TD>()
           where TD : class, new()
           where TS : class, ISaga<TD>;

        IBusConfigurator WithOutboxProcessorOptions(OutboxProcessorOptions options);

        IServiceCollection Services { get; }
    }
}