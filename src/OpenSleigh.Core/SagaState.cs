using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core
{
    //TODO: get rid of Newtonsoft.JSON dependency
    public abstract class SagaState
    {
        private readonly List<IMessage> _outbox = new();

        [JsonProperty] //TODO: can we use an HashSet here ?
        private readonly Dictionary<Guid, IMessage> _processedMessages = new();

        [JsonProperty] 
        private bool _isComplete;
        
        protected SagaState(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        
        [JsonIgnore]
        public IReadOnlyCollection<IMessage> Outbox => _outbox;

        public void SetAsProcessed<TM>(TM message) where TM : IMessage
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            if (this.Id != message.CorrelationId)
                throw new ArgumentException($"invalid message correlation id", nameof(message));
            
            _processedMessages[message.Id] = message;
        }

        public bool CheckWasProcessed<TM>(TM message) where TM : IMessage
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return _processedMessages.ContainsKey(message.Id);
        }

        public bool IsCompleted() => _isComplete;

        public void MarkAsCompleted() => _isComplete = true;

        internal void AddToOutbox<TM>(TM message) where TM : IMessage
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            _outbox.Add(message);
        }

        internal async Task PersistOutboxAsync(IOutboxRepository outboxRepository, CancellationToken cancellationToken = default)
        {
            foreach (var message in _outbox)
                await outboxRepository.AppendAsync(message, cancellationToken);
            _outbox.Clear();
        }
    }
}