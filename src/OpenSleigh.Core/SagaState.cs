using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    //TODO: get rid of Newtonsoft.JSON dependency
    public abstract class SagaState
    {
        [JsonProperty]
        private readonly Queue<IMessage> _outbox = new Queue<IMessage>();

        [JsonIgnore]
        private readonly HashSet<Guid> _outboxIds = new HashSet<Guid>();

        [JsonProperty]
        private readonly List<ProcessedMessage> _processedMessages = new();


        protected SagaState(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }

        [JsonIgnore]
        public IReadOnlyCollection<IMessage> Outbox => _outbox;

        [JsonIgnore]
        public IReadOnlyCollection<ProcessedMessage> ProcessedMessages => _processedMessages;

        public void EnqueueMessage(IMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (_outboxIds.Contains(message.Id))
                throw new ArgumentException($"message '{message.Id}' was already enqueued", nameof(message));

            _outbox.Enqueue(message);
            _outboxIds.Add(message.Id);
        }

        public async Task<IEnumerable<Exception>> ProcessOutboxAsync(IMessageBus bus, CancellationToken cancellationToken = default)
        {
            var failedMessages = new Queue<IMessage>();
            var exceptions = new List<Exception>();

            while (_outbox.Any())
            {
                var message = _outbox.Dequeue();
                try
                {
                    await bus.PublishAsync((dynamic)message, cancellationToken);
                    _processedMessages.Add(new ProcessedMessage(message, DateTime.UtcNow));
                }
                catch (Exception e)
                {
                    failedMessages.Enqueue(message);
                    exceptions.Add(e);
                }
            }

            _outboxIds.Clear();

            while (failedMessages.Any())
            {
                var message = failedMessages.Dequeue();
                EnqueueMessage(message);
            }

            return exceptions; //TODO: evaluate returning a proper Error class instead of Exception
        }
    }

    public record ProcessedMessage(IMessage Message, DateTime When);
}