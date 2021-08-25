using System;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class QueueReferenceFactoryTests
    {
        [Fact]
        public void Create_should_use_default_creator_when_none_defined()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp, messageType =>
            {
                var topicName = messageType.Name.ToLower();
                return new QueueReferences(topicName, topicName + ".dead");
            });
            
            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.DeadLetterTopicName.Should().Be("dummymessage.dead");
        }

        [Fact]
        public void Create_should_use_registered_creator()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var policy = new QueueReferencesPolicy<DummyMessage>(() => new QueueReferences("dummy", "dummy.dead"));

            sp.GetService(typeof(QueueReferencesPolicy<DummyMessage>))
                .Returns(policy);
            var sut = new QueueReferenceFactory(sp);
            
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummy");
            result.DeadLetterTopicName.Should().Be("dummy.dead");
        }

        [Fact]
        public void Create_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);
            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.DeadLetterTopicName.Should().Be("dummymessage.dead");
        }

        [Fact]
        public void Create_generic_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.DeadLetterTopicName.Should().Be("dummymessage.dead");
        }

        [Fact]
        public void ctor_should_throw_if_service_provider_null()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null));
        }

        [Fact]
        public void GetQueueType_should_throw_when_input_invalid()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);

            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType(null));
            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType(""));
            Assert.Throws<ArgumentNullException>(() => sut.GetQueueType("   "));
        }

        [Fact]
        public void GetQueueType_should_return_null_when_type_not_found()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);

            var result = sut.GetQueueType("invalid topic name");
            result.Should().BeNull();
        }

        [Fact]
        public void GetQueueType_should_return_type_when_input_valid()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);

            var queueRef = sut.Create<DummyMessage>();
            queueRef.Should().NotBeNull();
            
            var result = sut.GetQueueType(queueRef.TopicName);
            result.Should().Be(typeof(DummyMessage));
        }
    }
}
