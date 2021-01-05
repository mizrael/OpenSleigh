using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface ISagaConfigurator<TS, in TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        IServiceCollection Services { get; }
        ISagaConfigurator<TS, TD> UseStateFactory(Func<IMessage, TD> stateFactory);
    }

    internal class SagaConfigurator<TS, TD> : ISagaConfigurator<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        public SagaConfigurator(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IServiceCollection Services { get; }

        public ISagaConfigurator<TS, TD> UseStateFactory(Func<IMessage, TD> stateFactory)
        {
            var stateType = typeof(TD);
            var factory = new LambdaSagaStateFactory<TD>(stateFactory);

            var descriptor = ServiceDescriptor.Singleton(typeof(ISagaStateFactory<>).MakeGenericType(stateType), factory);
            this.Services.Replace(descriptor);

            return this;
        }
    }
}