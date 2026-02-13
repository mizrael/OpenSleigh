using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Utils;

public class TypeExtensionsTests
{
    [Fact]
    public void GetHandledMessageTypes_should_return_all_handled_types()
    {
        var types = typeof(FakeSaga).GetHandledMessageTypes().ToList();

        Assert.Contains(typeof(FakeSagaStarter), types);
        Assert.Contains(typeof(OtherFakeSagaStarter), types);
        Assert.Contains(typeof(FakeSagaMessage), types);
    }

    [Fact]
    public void GetHandledMessageTypes_should_return_empty_for_type_without_handlers()
    {
        var types = typeof(string).GetHandledMessageTypes().ToList();
        Assert.Empty(types);
    }

    [Fact]
    public void GetInitiatorMessageType_should_return_IStartedBy_types()
    {
        var types = typeof(FakeSaga).GetInitiatorMessageType();

        Assert.Contains(typeof(FakeSagaStarter), types);
        Assert.Contains(typeof(OtherFakeSagaStarter), types);
        Assert.DoesNotContain(typeof(FakeSagaMessage), types);
    }

    [Fact]
    public void GetInitiatorMessageType_should_return_empty_for_type_without_starters()
    {
        var types = typeof(FakeSagaNoStarter).GetInitiatorMessageType();
        Assert.Empty(types);
    }
}
