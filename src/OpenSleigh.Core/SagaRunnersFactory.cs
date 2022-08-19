using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public class SagaRunnersFactory : ISagaRunnersFactory
    {
        private readonly ISagaTypeResolver _stateTypeResolver;
        private readonly ITypesCache _typesCache;

        public SagaRunnersFactory(ISagaTypeResolver stateTypeResolver, ITypesCache typesCache)
        {     
            _stateTypeResolver = stateTypeResolver ?? throw new ArgumentNullException(nameof(stateTypeResolver));
            _typesCache = typesCache ?? throw new ArgumentNullException(nameof(typesCache));
        }

        /// <inheritdoc/>
        public IEnumerable<ISagaRunner> Create<TM>(IServiceScope scope)
            where TM : IMessage
        {
            var sagaTypes = _stateTypeResolver.Resolve<TM>();
            if (null == sagaTypes)
                return Enumerable.Empty<ISagaRunner>();

            var runners = new List<ISagaRunner>();

            foreach (var types in sagaTypes)
            {
                var runnerType = _typesCache.GetGeneric(typeof(ISagaRunner<,>), types.sagaType, types.sagaStateType);
                var runner = (ISagaRunner)scope.ServiceProvider.GetService(runnerType);
                if (runner is not null)
                    runners.Add(runner);
            }

            return runners;
        }
    }
}