using System;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class MessagePolicyFactoryTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            Assert.Throws<ArgumentNullException>(() => new MessagePolicyFactory<DummySaga, StartDummySaga>(null));
        }

        [Fact]
        public void Create_should_return_policy()
        {
            var expectedFactory = NSubstitute.Substitute.For<IPolicy>(); 
            var builder = NSubstitute.Substitute.For<IPolicyBuilder>();
            builder.Build().Returns(expectedFactory);
            
            var sut = new MessagePolicyFactory<DummySaga, StartDummySaga>(builder);
            var result = sut.Create();
            result.Should().Be(expectedFactory);
        }
    }
}