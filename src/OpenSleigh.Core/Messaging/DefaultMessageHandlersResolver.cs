using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Core.Messaging
{
    internal class DefaultMessageHandlersResolver : IMessageHandlersResolver
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultMessageHandlersResolver(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IEnumerable<IHandleMessage<TM>> Resolve<TM>() where TM : IMessage
        {
            var handlers = _serviceProvider.GetService<IEnumerable<IHandleMessage<TM>>>()
                           ?? Enumerable.Empty<IHandleMessage<TM>>();

            foreach (var handler in handlers)
            {
                if (!handler.GetType().IsSaga())
                    yield return handler;
            }
        }
    }
}