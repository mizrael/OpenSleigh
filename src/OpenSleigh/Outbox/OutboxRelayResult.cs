namespace OpenSleigh.Outbox;

/// <summary>
/// outcome of one pass over the pending outbox messages.
/// </summary>
/// <param name="Relayed">messages published and removed from the outbox.</param>
/// <param name="Failed">messages that were read but are still in the outbox.</param>
public readonly record struct OutboxRelayResult(int Relayed, int Failed)
{
    public static readonly OutboxRelayResult Nothing = new(0, 0);
}
