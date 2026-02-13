using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Utils;

public class TypeResolverTests
{
    [Fact]
    public void Register_should_throw_when_type_is_null()
    {
        var sut = new TypeResolver();
        Assert.Throws<ArgumentNullException>(() => sut.Register(null!));
    }

    [Fact]
    public void Resolve_should_return_registered_type_by_name()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));

        var result = sut.Resolve(nameof(FakeSagaStarter));

        Assert.Equal(typeof(FakeSagaStarter), result);
    }

    [Fact]
    public void Resolve_should_be_case_insensitive()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));

        var result = sut.Resolve("fakesagastarter");

        Assert.Equal(typeof(FakeSagaStarter), result);
    }

    [Fact]
    public void Resolve_should_throw_TypeLoadException_when_type_not_found_and_throwOnError()
    {
        var sut = new TypeResolver();

        Assert.Throws<TypeLoadException>(() => sut.Resolve("NonExistentType"));
    }

    [Fact]
    public void Resolve_should_return_null_when_type_not_found_and_throwOnError_is_false()
    {
        var sut = new TypeResolver();

        var result = sut.Resolve("NonExistentType", throwOnError: false);

        Assert.Null(result);
    }

    [Fact]
    public void Register_should_not_duplicate_assembly_on_second_call()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));
        sut.Register(typeof(FakeSagaStarter));

        var result = sut.Resolve(nameof(FakeSagaStarter));
        Assert.Equal(typeof(FakeSagaStarter), result);
    }

    [Fact]
    public void Resolve_should_find_type_by_full_name_via_assembly_scan()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));

        var result = sut.Resolve(typeof(FakeSagaMessage).FullName!);

        Assert.Equal(typeof(FakeSagaMessage), result);
    }
}
