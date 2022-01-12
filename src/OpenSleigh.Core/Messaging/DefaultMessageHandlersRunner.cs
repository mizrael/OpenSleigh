using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Messaging
{
    internal class DefaultMessageHandlersRunner : IMessageHandlersRunner
    {
        private readonly IMessageHandlersResolver _messageHandlersResolver;

        public DefaultMessageHandlersRunner(IMessageHandlersResolver messageHandlersResolver)
        {
            _messageHandlersResolver = messageHandlersResolver ?? throw new System.ArgumentNullException(nameof(messageHandlersResolver));
        }

        public async Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default) where TM : IMessage
        {
            var handlers = _messageHandlersResolver.Resolve<TM>();
            if (handlers is null)
                return;

            foreach (var handler in handlers)
                await handler.HandleAsync(messageContext, cancellationToken);
        }
    }
}