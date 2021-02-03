using System;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class RetryPolicyBuilderTests
    {
        [Fact]
        public void WithMaxRetries_should_set_max_retries()
        {
            var sut = new RetryPolicyBuilder();
            
            sut.MaxRetries.Should().Be(1);
            
            sut.WithMaxRetries(42);

            sut.MaxRetries.Should().Be(42);
        }

        [Fact]
        public void Handle_should_register_Exception()
        {
            var sut = new RetryPolicyBuilder();

            var ex = new ArgumentException();
            sut.ExceptionFilters.CanHandle(ex).Should().BeFalse();
            sut.Handle<ArgumentException>();
            sut.ExceptionFilters.CanHandle(ex).Should().BeTrue();
        }

        [Fact]
        public void OnException_should_add_handler()
        {
            var sut = new RetryPolicyBuilder();
            
            sut.OnExceptionHandler.Should().BeNull();

            var handler = new OnExceptionHandler(ctx => Task.CompletedTask);
            sut.OnException(handler);
            sut.OnExceptionHandler.Should().Be(handler);
        }
    }
}