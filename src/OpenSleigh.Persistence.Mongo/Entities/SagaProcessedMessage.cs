namespace OpenSleigh.Persistence.Mongo.Entities
{
    public class SagaProcessedMessage
    {
        public required string InstanceId { get; init; }
        public required string MessageId { get; init; }
        public required DateTimeOffset When { get; init; }
    }
}
