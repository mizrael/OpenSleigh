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
                return new QueueReferences(topicName);
            });
            
            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
        }

        [Fact]
        public void Create_should_use_registered_creator()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var policy = new QueueReferencesPolicy<DummyMessage>(() =>
            {
                var topicName = "dummy";
                return new QueueReferences(topicName);
            });
            sp.GetService(typeof(QueueReferencesPolicy<DummyMessage>))
                .Returns(policy);
            var sut = new QueueReferenceFactory(sp);
            
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummy");
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
        }

        [Fact]
        public void Create_generic_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
        }

        [Fact]
        public void ctor_should_throw_if_service_provider_null()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null));
        }
    }
}
