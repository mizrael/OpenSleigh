using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//TODO: rename the namespace and the Core assembly to 'OpenSleigh"
namespace OpenSleigh.Core
{
    public abstract class Saga<TD> : ISaga
        where TD : SagaState
    {
        private readonly List<IMessage> _outbox = new();

        public TD State { get; }

        protected Saga(TD state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        protected void Publish<TM>(TM message) where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            _outbox.Add(message);
        }

        internal async Task PersistOutboxAsync(IOutboxRepository outboxRepository, CancellationToken cancellationToken)
        {
            if (outboxRepository is null)            
                throw new ArgumentNullException(nameof(outboxRepository));

            if (!_outbox.Any())
                return;

            await outboxRepository.AppendAsync(_outbox, cancellationToken)
                                  .ConfigureAwait(false);

            _outbox.Clear();
        }
    }
}