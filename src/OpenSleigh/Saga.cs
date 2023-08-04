using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh
{
    public abstract class Saga : ISaga
    {
        private readonly ISerializer _serializer;
        private readonly ISagaExecutionContext _context;

        //TODO: I don't like the serializer here
        protected Saga(ISagaExecutionContext context, ISerializer serializer)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        protected void Publish<TM>(TM message)
            where TM : IMessage
        {
            ArgumentNullException.ThrowIfNull(message);

            var outboxMessage = OutboxMessage.Create(message, _serializer, this.Context);
            _context.Publish(outboxMessage);
        }

        public ISagaExecutionContext Context => _context;
    }

    public abstract class Saga<TS> : Saga, ISaga<TS>
        where TS : new()
    {
        private readonly ISagaExecutionContext<TS> _context;

        protected Saga(ISagaExecutionContext<TS> context, ISerializer serializer)
            : base(context, serializer)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public new ISagaExecutionContext<TS> Context => _context;
        ISagaExecutionContext ISaga.Context => _context;
    }
}