using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public class SagasRunner : ISagasRunner
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ISagaTypeResolver _stateTypeResolver;
        private readonly ITypesCache _typesCache;

        public SagasRunner(IServiceProvider serviceProvider, ISagaTypeResolver stateTypeResolver, ITypesCache typesCache)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _stateTypeResolver = stateTypeResolver ?? throw new ArgumentNullException(nameof(stateTypeResolver));
            _typesCache = typesCache ?? throw new ArgumentNullException(nameof(typesCache));
        }

        public async Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (messageContext == null)
                throw new ArgumentNullException(nameof(messageContext));
            
            var sagaTypes = _stateTypeResolver.Resolve<TM>();
            if (null == sagaTypes || !sagaTypes.Any())
                throw new SagaNotFoundException($"no Saga registered for message of type '{typeof(TM).FullName}'");

            var exceptions = new List<Exception>();
            
            foreach (var types in sagaTypes)
            {
                try
                {
                    var runnerType = _typesCache.GetGeneric(typeof(ISagaRunner<,>), types.sagaType, types.sagaStateType);
                    var runner = _serviceProvider.GetService(runnerType);
                    if (null != runner)
                        await RunAsyncCore(messageContext, runner, cancellationToken);
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            if (exceptions.Any())
                throw new AggregateException($"an error has occurred while processing message '{messageContext.Message.Id}'", 
                                                exceptions);
        }

        private static async Task RunAsyncCore<TM>(IMessageContext<TM> messageContext, object runner, 
            CancellationToken cancellationToken) where TM : IMessage
        {
            await (runner as dynamic).RunAsync(messageContext, cancellationToken);
        }
    }
}