using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public class SagasRunner : ISagasRunner
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISagaTypeResolver _stateTypeResolver;
        private readonly ITypesCache _typesCache;

        public SagasRunner(IServiceScopeFactory scopeFactory, ISagaTypeResolver stateTypeResolver, ITypesCache typesCache)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _stateTypeResolver = stateTypeResolver ?? throw new ArgumentNullException(nameof(stateTypeResolver));
            _typesCache = typesCache ?? throw new ArgumentNullException(nameof(typesCache));
        }

        public Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (messageContext == null)
                throw new ArgumentNullException(nameof(messageContext));
            
            var sagaTypes = _stateTypeResolver.Resolve<TM>();
            if (null == sagaTypes)
                return Task.CompletedTask;

           return RunAsyncCore(messageContext, sagaTypes, cancellationToken);
        }

        private async Task RunAsyncCore<TM>(IMessageContext<TM> messageContext,
            IEnumerable<(Type sagaType, Type sagaStateType)> sagaTypes, 
            CancellationToken cancellationToken) where TM : IMessage
        {
            var exceptions = new List<Exception>();

            foreach (var types in sagaTypes)
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var runnerType = _typesCache.GetGeneric(typeof(ISagaRunner<,>), types.sagaType, types.sagaStateType);
                    var runner = (ISagaRunner)scope.ServiceProvider.GetService(runnerType);
                    if (null != runner)
                        await runner.RunAsync(messageContext, cancellationToken);
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            if (exceptions.Any())
                throw new AggregateException(exceptions);
        }
    }
}