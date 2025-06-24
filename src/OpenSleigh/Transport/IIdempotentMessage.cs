using System.Text;

namespace OpenSleigh.Transport;

public interface IIdempotentMessage : IMessage, IHasRequestId
{
    public virtual string GetId()
    {
        var sb = new StringBuilder();

        sb.Append(this.RequestId);

        var components = GetIdempotencyComponents();
        foreach (var component in components)
        {
            sb.Append(component);
        }

        if(this is IHasCorrelationId hasCorrelationId)
        {
            sb.Append(hasCorrelationId.CorrelationId);
        }

        return sb.ToString();
    }

    IEnumerable<object> GetIdempotencyComponents();
}