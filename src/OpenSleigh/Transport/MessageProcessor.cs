using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport
{
    internal class MessageProcessor : IMessageProcessor
    {
        private readonly ISagaDescriptorsResolver _sagaDescriptorsResolver;
        private readonly ISagaRunner _sagaRunner;
        private readonly ISerializer _serializer;
        
        public MessageProcessor(
            ISagaRunner sagaRunner, 
            ISagaDescriptorsResolver sagaDescriptorsResolver,
            ISerializer serializer)
        {
            _sagaRunner = sagaRunner ?? throw new ArgumentNullException(nameof(sagaRunner));
            _sagaDescriptorsResolver = sagaDescriptorsResolver ?? throw new ArgumentNullException(nameof(sagaDescriptorsResolver));

            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async ValueTask ProcessAsync(OutboxMessage outboxMessage, CancellationToken cancellationToken = default)
        {
            if (outboxMessage is null)
                throw new ArgumentNullException(nameof(outboxMessage));

            var message = outboxMessage.GetMessage(_serializer);
            var messageContext = ToContext((dynamic)message, outboxMessage);

            var descriptors = _sagaDescriptorsResolver.Resolve(message);
            foreach (var descriptor in descriptors) //TODO: parallelize
            {                
                try
                {
                    await _sagaRunner.ProcessAsync(messageContext, descriptor, cancellationToken)
                                 .ConfigureAwait(false);
                }
                catch(SagaException)
                {
                    // TODO: send outboxMessage + descriptor to deadletter   
                }
            }
        }

        private static IMessageContext<TM> ToContext<TM>(TM message, OutboxMessage outboxMessage)
            where TM : IMessage
        {
            return MessageContext<TM>.Create(message, outboxMessage);
        }
    }
}