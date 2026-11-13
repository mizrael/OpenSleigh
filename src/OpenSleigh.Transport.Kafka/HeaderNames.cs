namespace OpenSleigh.Transport.Kafka;

public static class HeaderNames
{
    public const string Error = "error";

    /// <summary>
    /// on a message in a retry topic: the moment it can be processed again, in milliseconds since the Unix epoch.
    /// </summary>
    public const string NotBefore = "not-before";
}
