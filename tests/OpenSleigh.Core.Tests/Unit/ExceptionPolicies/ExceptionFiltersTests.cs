using System;
using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class ExceptionFiltersTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new ExceptionFilters(null));
        }

        [Fact]
        public void CanHandle_should_return_false_if_exception_cannot_be_handled()
        {
            var sut = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ArgumentNullException)
            });

            var ex = new ApplicationException();
            sut.CanHandle(ex).Should().BeFalse();
        }

        [Fact]
        public void CanHandle_should_return_true_if_exception_can_be_handled() 
        {
            var sut = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ArgumentNullException)
            });

            var ex = new ArgumentNullException();
            sut.CanHandle(ex).Should().BeTrue();
        }
    }
}
