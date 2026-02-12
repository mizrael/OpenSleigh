using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

internal interface ISagaStateExtractor
{
    byte[] Extract(ISagaInstance instance, ISerializer serializer);
}

internal sealed class SagaStateExtractor<TS> : ISagaStateExtractor
{
    public byte[] Extract(ISagaInstance instance, ISerializer serializer)
        => serializer.Serialize(((ISagaInstance<TS>)instance).State);
}
