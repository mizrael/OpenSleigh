using System;
using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class DelayedRetryPolicyBuilderTests
    {
        [Fact]
        public void WithDelayFactory_should_add_handler()
        {
            var sut = new DelayedRetryPolicyBuilder();
            
            var handler = new DelayFactory(i => TimeSpan.Zero);
            sut.WithDelayFactory(handler);
            
            sut.DelayFactory.Should().Be(handler);
        }
    }
}