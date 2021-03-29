using System;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class QueueReferenceFactoryTests
    {
        [Fact]
        public void Create_should_use_default_creator_when_none_defined()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            
            var sut = new QueueReferenceFactory(sp, sysInfo, messageType =>
            {
                var TopicName = messageType.Name.ToLower();
                var SubscriptionName = TopicName + ".a";
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
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            
            var policy = new QueueReferencesPolicy<DummyMessage>(() =>
            {
                var TopicName = "dummy";
                var SubscriptionName = TopicName + ".a";
                return new QueueReferences(TopicName, SubscriptionName);
            });
            sp.GetService(typeof(QueueReferencesPolicy<DummyMessage>))
                .Returns(policy);
            var sut = new QueueReferenceFactory(sp, sysInfo);

            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummy");
            result.SubscriptionName.Should().Be("dummy.a");
        }

        [Fact]
        public void Create_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            var sut = new QueueReferenceFactory(sp, sysInfo);
            
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.SubscriptionName.Should().Be("dummymessage.workers");
        }

        [Fact]
        public void Create_generic_should_return_valid_references()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            var sut = new QueueReferenceFactory(sp, sysInfo);
            var result = sut.Create<DummyMessage>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummymessage");
            result.SubscriptionName.Should().Be("dummymessage.workers");
        }
        
        [Fact]
        public void Create_generic_should_return_valid_references_for_event()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            var sut = new QueueReferenceFactory(sp, sysInfo);
            var result = sut.Create<DummyEvent>();
            result.Should().NotBeNull();
            result.TopicName.Should().Be("dummyevent");
            result.SubscriptionName.Should().Be("dummyevent.test.workers");
        }

        [Fact]
        public void ctor_should_throw_if_service_provider_null()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(sp, null));
        }
    }
}
