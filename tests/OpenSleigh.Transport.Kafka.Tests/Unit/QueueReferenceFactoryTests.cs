using FluentAssertions;
using System;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
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
            
            var message = DummyMessage.CreateOutboxMessage();
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
            var message = DummyMessage.CreateOutboxMessage();
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

        [Fact]
        public void GetQueueType_should_throw_when_input_invalid()
        {
            var sut = new QueueReferenceFactory();

            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType(null));
            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType(""));
            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType("   "));
        }

        [Fact]
        public void GetQueueType_should_return_null_when_type_not_found()
        {
            var sut = new QueueReferenceFactory();

            var result = sut.GetQueueType("invalid topic name");
            result.Should().BeNull();
        }

        [Fact]
        public void GetQueueType_should_return_type_when_input_valid()
        {
            var sut = new QueueReferenceFactory();

            var queueRef = sut.Create<DummyMessage>();
            queueRef.Should().NotBeNull();
            
            var result = sut.GetQueueType(queueRef.TopicName);
            result.Should().Be(typeof(DummyMessage));
        }
    }
}
