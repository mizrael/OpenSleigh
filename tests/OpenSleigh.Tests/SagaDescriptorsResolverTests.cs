using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests;

public class SagaDescriptorsResolverTests
{
    [Fact]
    public void Ctor_should_throw_when_typeResolver_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SagaDescriptorsResolver(null!));
    }

    [Fact]
    public void Register_stateless_should_register_all_handled_message_types()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        sut.Register<FakeSaga>();

        var registeredTypes = sut.GetRegisteredMessageTypes().ToList();
        Assert.Contains(typeof(FakeSagaStarter), registeredTypes);
        Assert.Contains(typeof(OtherFakeSagaStarter), registeredTypes);
        Assert.Contains(typeof(FakeSagaMessage), registeredTypes);
    }

    [Fact]
    public void Register_stateful_should_register_all_handled_message_types()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        sut.Register<FakeSagaWithState, int>();

        var registeredTypes = sut.GetRegisteredMessageTypes().ToList();
        Assert.Contains(typeof(FakeSagaStarter), registeredTypes);
    }

    [Fact]
    public void Resolve_should_return_descriptors_for_registered_message()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        sut.Register<FakeSaga>();

        var message = new FakeSagaStarter();
        var descriptors = sut.Resolve(message).ToList();

        Assert.Single(descriptors);
        Assert.Equal(typeof(FakeSaga), descriptors[0].SagaType);
    }

    [Fact]
    public void Resolve_should_return_empty_for_unregistered_message()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        var message = new FakeSagaStarter();
        var descriptors = sut.Resolve(message).ToList();

        Assert.Empty(descriptors);
    }

    [Fact]
    public void Resolve_should_throw_when_message_is_null()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        Assert.Throws<ArgumentNullException>(() => sut.Resolve(null!));
    }

    [Fact]
    public void Register_should_add_multiple_sagas_for_same_message_type()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        sut.Register<FakeSaga>();
        sut.Register<FakeSagaWithState, int>();

        var message = new FakeSagaStarter();
        var descriptors = sut.Resolve(message).ToList();

        Assert.Equal(2, descriptors.Count);
    }

    [Fact]
    public void Register_should_call_typeResolver_Register_for_each_message_type()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var sut = new SagaDescriptorsResolver(typeResolver);

        sut.Register<FakeSaga>();

        typeResolver.Received().Register(typeof(FakeSagaStarter));
        typeResolver.Received().Register(typeof(OtherFakeSagaStarter));
        typeResolver.Received().Register(typeof(FakeSagaMessage));
    }
}
