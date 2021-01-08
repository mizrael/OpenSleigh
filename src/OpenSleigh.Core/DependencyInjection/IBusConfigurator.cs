using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface IBusConfigurator
    {
        ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
            where TS : Saga<TD>
            where TD : SagaState;

        IServiceCollection Services { get; }
    }
}