using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class PolicyTests
    {
        [Fact]
        public void Retry_should_build_retry_policy()
        {
            var policy = Policy.Retry(builder => { });
            policy.Should().NotBeNull()
                .And.BeOfType<RetryPolicy>();
        }

    }
}