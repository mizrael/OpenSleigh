namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class QueueReferenceFactoryTests
{
    [Fact]
    public void Create_should_use_default_creator_when_none_defined()
    {
        var sut = new QueueReferenceFactory(messageType =>
        {
            var topicName = messageType.Name.ToLower();
            return new QueueReferences(topicName, topicName + ".dead");
        });
        
        var message = DummyMessage.CreateEnvelope();
        var result = sut.Create(message);
        result.Should().NotBeNull();
        result.TopicName.Should().Be("dummymessage");
        result.DeadLetterTopicName.Should().Be("dummymessage.dead");
    }

    [Fact]
    public void Create_should_return_valid_references()
    {
        var queueRef = new QueueReferences("dummymessage", "dummymessage.dead");
        QueueReferencesCreator creator = messageType =>
        {
            var topicName = messageType.Name.ToLower();
            return new QueueReferences(topicName, topicName + ".dead");
        };
        var sp = NSubstitute.Substitute.For<IServiceProvider>();
        var sut = new QueueReferenceFactory(creator);
        var message = DummyMessage.CreateEnvelope();
        var result = sut.Create(message);
        result.Should().NotBeNull();
        result.TopicName.Should().Be("dummymessage");
        result.DeadLetterTopicName.Should().Be("dummymessage.dead");
    }

    [Fact]
    public void Create_generic_should_return_valid_references()
    {
        var sut = new QueueReferenceFactory();
        var result = sut.Create<DummyMessage>();
        result.Should().NotBeNull();
        result.TopicName.Should().Be("dummymessage");
        result.DeadLetterTopicName.Should().Be("dummymessage.dead");
    }
}