using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh;

public abstract class Saga : ISaga
{
    private readonly ISagaInstance _context;

    protected Saga(ISagaInstance context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    protected void Publish<TM>(TM message)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        var outboxMessage = MessageEnvelope.Create(message, this.Context);
        _context.Publish(outboxMessage);
    }

    public ISagaInstance Context => _context;
}

public abstract class Saga<TS> : Saga, ISaga<TS>
    where TS : new()
{
    private readonly ISagaInstance<TS> _context;

    protected Saga(ISagaInstance<TS> context)
        : base(context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public new ISagaInstance<TS> Context => _context;
    ISagaInstance ISaga.Context => _context;
}