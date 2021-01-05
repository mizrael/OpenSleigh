using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Exceptions;
using System;
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

        public Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (messageContext == null)
                throw new ArgumentNullException(nameof(messageContext));

            //TODO: an event can be handled by multiple sagas
            var types = _stateTypeResolver.Resolve<TM>();
            if (default == types)
                throw new SagaNotFoundException($"no saga registered for message of type '{typeof(TM).FullName}'");

            var runnerType = _typesCache.GetGeneric(typeof(ISagaRunner<,>), types.sagaType, types.sagaStateType);
            var runner = _serviceProvider.GetService(runnerType);
            if (null == runner)
                throw new SagaNotFoundException($"no saga registered on DI for message of type '{typeof(TM).FullName}'");

            return RunAsyncCore(messageContext, runnerType, runner, cancellationToken);
        }

        private async Task RunAsyncCore<TM>(IMessageContext<TM> messageContext,
            Type runnerType, object runner, 
            CancellationToken cancellationToken) where TM : IMessage
        {
            var genericHandlerMethod = _typesCache.GetMethod(runnerType, nameof(ISagaRunner<Saga<SagaState>, SagaState>.RunAsync));
            var handlerMethod = genericHandlerMethod.MakeGenericMethod(typeof(TM));

            await (Task) handlerMethod.Invoke(runner, new[] {(object) messageContext, (object) cancellationToken});
        }
    }
}