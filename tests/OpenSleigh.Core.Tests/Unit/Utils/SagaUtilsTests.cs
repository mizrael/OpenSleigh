using FluentAssertions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Utils
{
    public class SagaUtilsTests
    {
        [Fact]
        public void GetHandledMessageTypes_should_return_empty_collection_when_no_messages_handled()
        {
            var results = SagaUtils<EmptySaga, SagaState>.GetHandledMessageTypes();
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void GetHandledMessageTypes_should_return_handled_messages_types()
        {
            var results = SagaUtils<DummySaga, DummySagaState>.GetHandledMessageTypes();
            results.Should().NotBeNullOrEmpty().And.HaveCount(2);
            results.Should().Contain(new[]{typeof(StartDummySaga), typeof(DummySagaStarted)});
        }
    }
    
    internal class EmptySaga : Saga<SagaState> { }
}
