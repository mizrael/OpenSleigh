namespace OpenSleigh
{
    public interface ISystemInfo
    {
        string ClientGroup { get; }
        string ClientId { get; }
        bool PublishOnly { get; }
    }
}