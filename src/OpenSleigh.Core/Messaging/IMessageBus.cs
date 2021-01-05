using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.Messaging
{
    public interface IMessageBus
    {
        Task PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
        void SetTransaction(ITransaction transaction);
    }

    internal class DefaultMessageBus : IMessageBus
    {
        private readonly IOutboxRepository _outboxRepository;
        private ITransaction _transaction;
        
        public DefaultMessageBus(IOutboxRepository outboxRepository)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        }

        public Task PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore(message, cancellationToken);
        }

        private Task PublishAsyncCore<TM>(TM message, CancellationToken cancellationToken) 
            where TM : IMessage
        => _outboxRepository.AppendAsync(message, _transaction, cancellationToken);

        public void SetTransaction(ITransaction transaction) => _transaction = transaction;
    }
}