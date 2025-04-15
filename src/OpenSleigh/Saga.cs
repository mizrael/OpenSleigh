using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh;

public abstract class Saga : ISaga
{
    private readonly ISagaExecutionContext _context;

    protected Saga(ISagaExecutionContext context)
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

    public ISagaExecutionContext Context => _context;
}

public abstract class Saga<TS> : Saga, ISaga<TS>
    where TS : new()
{
    private readonly ISagaExecutionContext<TS> _context;

    protected Saga(ISagaExecutionContext<TS> context)
        : base(context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public new ISagaExecutionContext<TS> Context => _context;
    ISagaExecutionContext ISaga.Context => _context;
}