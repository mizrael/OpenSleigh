using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class SagaConfigurator<TS, TD> : ISagaConfigurator<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        public SagaConfigurator(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IServiceCollection Services { get; }

        public ISagaConfigurator<TS, TD> UseStateFactory<TM>(Func<TM, TD> stateFactory)
            where TM : IMessage
        {
            var messageType = typeof(TM);
            var stateType = typeof(TD);

            var factoryInterfaceType = typeof(ISagaStateFactory<,>).MakeGenericType(messageType, stateType);
            var factory = new LambdaSagaStateFactory<TM, TD>(stateFactory);

            var descriptor = ServiceDescriptor.Singleton(factoryInterfaceType, factory);
            this.Services.Replace(descriptor);

            return this;
        }

        public ISagaConfigurator<TS, TD> UseRetryPolicy<TM>(Action<IRetryPolicyBuilder> builderAction)
            where TM : IMessage
        {
            if (builderAction == null) 
                throw new ArgumentNullException(nameof(builderAction));
            
            var builder = new RetryPolicyBuilder();
            builderAction(builder);
            
            this.Services.AddTransient<IMessagePolicyFactory<TS, TM>>(_ => new MessagePolicyFactory<TS, TM>(builder));
            
            return this;
        }
    }
}