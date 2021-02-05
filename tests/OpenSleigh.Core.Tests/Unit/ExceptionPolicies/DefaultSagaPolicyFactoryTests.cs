using System;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class DefaultSagaPolicyFactoryTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            Assert.Throws<ArgumentNullException>(() => new DefaultSagaPolicyFactory<DummySaga>(null));
        }

        [Fact]
        public void Create_should_return_null_when_policy_not_configured()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            
            var sut = new DefaultSagaPolicyFactory<DummySaga>(sp);
            var policy = sut.Create<StartDummySaga>();
            policy.Should().BeNull();
        }

        [Fact]
        public void Create_should_return_policy_when_policy_not_configured()
        {
            var expectedPolicy = NSubstitute.Substitute.For<IPolicy>();
            var factory = NSubstitute.Substitute.For<IMessagePolicyFactory<DummySaga, StartDummySaga>>();
            factory.Create().Returns(expectedPolicy);
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IMessagePolicyFactory<DummySaga, StartDummySaga>))
                .Returns(factory);

            var sut = new DefaultSagaPolicyFactory<DummySaga>(sp);
            var policy = sut.Create<StartDummySaga>();
            policy.Should().Be(expectedPolicy);
        }
    }
}