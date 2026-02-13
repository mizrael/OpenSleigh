using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class DefaultMessageHandlerFactoryTests
{
    [Fact]
    public void Create_should_return_handler_for_valid_saga()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var sut = new DefaultMessageHandlerFactory(serviceProvider);

        var handler = sut.Create<FakeSagaStarter>(instance);

        Assert.NotNull(handler);
        Assert.IsType<FakeSaga>(handler);
    }

    [Fact]
    public void Ctor_should_throw_when_serviceProvider_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultMessageHandlerFactory(null!));
    }
}
