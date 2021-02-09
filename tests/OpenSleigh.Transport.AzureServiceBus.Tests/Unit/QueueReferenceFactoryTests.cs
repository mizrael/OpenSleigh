using System;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class QueueReferenceFactoryTests
    {
        [Fact]
        public void Create_should_use_default_creator_when_none_defined()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp, messageType =>
            {
                var TopicName = messageType.Name.ToLower();
                var SubscriptionName = TopicName + ".a";
                var dlTopicName = TopicName + ".b";
                var dlSubscriptionName = dlTopicName + ".c";
                return new QueueReferences(TopicName, SubscriptionName);
            });

            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.SubscriptionName.Should().Be("dummymessage.a");
        }

        [Fact]
        public void Create_should_use_registered_creator()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var policy = new QueueReferencesPolicy<DummyMessage>(() =>
            {
                var TopicName = "dummy";
                var SubscriptionName = TopicName + ".a";
                return new QueueReferences(TopicName, SubscriptionName);
            });
            sp.GetService(typeof(QueueReferencesPolicy<DummyMessage>))
                .Returns(policy);
            var sut = new QueueReferenceFactory(sp);

            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummy");
            result.SubscriptionName.Should().Be("dummy.a");
        }

        [Fact]
        public void Create_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);
            
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.SubscriptionName.Should().Be("dummymessage.workers");
        }

        [Fact]
        public void Create_generic_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new QueueReferenceFactory(sp);
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.SubscriptionName.Should().Be("dummymessage.workers");
        }

        [Fact]
        public void ctor_should_throw_if_service_provider_null()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null));
        }
    }
}
