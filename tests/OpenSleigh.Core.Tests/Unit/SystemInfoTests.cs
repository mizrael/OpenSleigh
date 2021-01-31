using FluentAssertions;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SystemInfoTests
    {
        [Fact]
        public void New_should_create_valid_instance()
        {
            var sut = SystemInfo.New();
            sut.Should().NotBeNull();
            sut.ClientId.Should().NotBeEmpty();
            sut.PublishOnly.Should().BeFalse();
            sut.ClientGroup.Should().NotBeNullOrWhiteSpace();
        }
    }
}
