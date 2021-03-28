using FluentAssertions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using Xunit;
using TypeExtensions = OpenSleigh.Core.Utils.TypeExtensions;

namespace OpenSleigh.Core.Tests.Unit.Utils
{
    public class TypeExtensionsTests
    {
        [Fact]
        public void GetHandledMessageTypes_should_return_empty_collection_when_no_messages_handled()
        {
            var results = TypeExtensions.GetHandledMessageTypes(typeof(EmptySaga));
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void GetHandledMessageTypes_should_return_handled_messages_types()
        {
            var results = TypeExtensions.GetHandledMessageTypes(typeof(DummySaga));
            results.Should().NotBeNullOrEmpty().And.HaveCount(2);
            results.Should().Contain(new[]{typeof(StartDummySaga), typeof(DummySagaStarted)});
        }

        [Fact]
        public void IsSaga_should_return_false_when_type_is_not_saga_class()
        {
            typeof(int).IsSaga().Should().BeFalse();
            typeof(TypeExtensionsTests).IsSaga().Should().BeFalse();
        }

        [Fact]
        public void IsSaga_should_return_false_when_type_is_message_handler()
        {
            typeof(DummyMessageHandler).IsSaga().Should().BeFalse();
        }

        [Fact]
        public void IsSaga_should_return_true_when_type_is_saga_class()
        {
            typeof(EmptySaga).IsSaga().Should().BeTrue();
        }

        [Fact]
        public void CanHandleMessage_should_return_false_when_type_cannot_handle_message()
        {
            typeof(int).CanHandleMessage<DummyMessage>().Should().BeFalse();
            typeof(TypeExtensionsTests).CanHandleMessage<DummyMessage>().Should().BeFalse();
            typeof(EmptySaga).CanHandleMessage<DummyMessage>().Should().BeFalse();
        }
        
        [Fact]
        public void CanHandleMessage_should_return_true_when_type_can_handle_message()
        {
            typeof(DummyMessageHandler).CanHandleMessage<DummyMessage>().Should().BeTrue();
            typeof(SimpleSaga).CanHandleMessage<StartSimpleSaga>().Should().BeTrue();
        }
    }
}
