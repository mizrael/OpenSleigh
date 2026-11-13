namespace OpenSleigh.Transport.Kafka;

public static class HeaderNames
{
    public const string Error = "error";

    /// <summary>
    /// on a message in a retry topic: the moment it can be processed again, in milliseconds since the Unix epoch.
    /// </summary>
    public const string NotBefore = "not-before";

    /// <summary>
    /// on a message in a retry topic: the client group that asked for the message to be processed again.
    /// The other groups that consume the retry topic leave it alone.
    /// </summary>
    public const string RetryGroup = "retry-group";
}
