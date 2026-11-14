namespace OpenSleigh;

/// <summary>
/// what a compensation handler is told about the failure it compensates.
/// </summary>
/// <param name="Exception">the exception the message handler threw.</param>
/// <param name="Attempts">how many times the handler was run for the message, the failed one included.</param>
public sealed record MessageFailure(Exception Exception, int Attempts);
