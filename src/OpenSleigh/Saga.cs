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

    /// <summary>
    /// publishes a message that is delivered not earlier than <paramref name="delay"/> from now.
    /// It is stored with the saga state, like any other published message, and stays in the outbox until it is due:
    /// how late it is delivered depends on how often the outbox is read.
    /// A saga that handles the message can use it as a timeout: when the saga is completed by then, it is not handled.
    /// </summary>
    /// <param name="delay">how long to wait before the message is delivered. Cannot be negative.</param>
    protected void Publish<TM>(TM message, TimeSpan delay)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        var outboxMessage = MessageEnvelope.Create(message, this.Context, delay);
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