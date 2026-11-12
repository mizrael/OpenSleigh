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

    [Fact]
    public void Resolve_should_cache_a_type_found_by_its_full_name()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));
        var cachedBefore = sut.CachedNamesCount;

        sut.Resolve(typeof(FakeSagaMessage).FullName!);
        sut.Resolve(typeof(FakeSagaMessage).FullName!.ToUpperInvariant());

        Assert.Equal(cachedBefore + 1, sut.CachedNamesCount);
    }

    [Fact]
    public void Resolve_should_not_cache_other_spellings_of_a_type_name()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));
        var fullName = typeof(FakeSagaMessage).FullName!;
        sut.Resolve(fullName);
        var cachedBefore = sut.CachedNamesCount;

        var spellings = Enumerable.Range(1, 20)
            .Select(rank => fullName + string.Concat(Enumerable.Repeat("[]", rank)))
            .Append(" " + fullName)
            .Append(fullName + "*")
            .Append(fullName + "&")
            .ToArray();
        var resolved = spellings.Count(spelling => sut.Resolve(spelling, throwOnError: false) is not null);

        Assert.True(resolved >= 20, $"only {resolved} spellings resolved: the test would prove nothing");
        Assert.Equal(cachedBefore, sut.CachedNamesCount);
    }

    [Fact]
    public void Resolve_should_not_cache_constructed_generic_types()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(GenericProbe<>));
        var cachedBefore = sut.CachedNamesCount;

        // a different closed type for every type argument: the names are canonical, and there is no end to them
        var closedTypes = new[]
        {
            typeof(GenericProbe<int>), typeof(GenericProbe<int[]>), typeof(GenericProbe<int[][]>),
            typeof(GenericProbe<string>), typeof(GenericProbe<GenericProbe<int>>), typeof(GenericProbe<GenericProbe<GenericProbe<long>>>)
        };
        foreach (var closedType in closedTypes)
            Assert.Equal(closedType, sut.Resolve(closedType.FullName!));

        Assert.Equal(cachedBefore, sut.CachedNamesCount);
    }

    [Fact]
    public void Register_should_keep_a_constructed_generic_type_resolvable_from_the_cache()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(GenericProbe<int>));
        var cachedBefore = sut.CachedNamesCount;

        Assert.Equal(typeof(GenericProbe<int>), sut.Resolve(typeof(GenericProbe<int>).FullName!));
        Assert.Equal(cachedBefore, sut.CachedNamesCount);
    }

    [Fact]
    public void Register_should_not_change_the_cache_when_the_type_is_already_known()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));
        var cachedBefore = sut.CachedNamesCount;

        sut.Register(typeof(FakeSagaStarter));

        Assert.Equal(cachedBefore, sut.CachedNamesCount);
        Assert.Equal(typeof(FakeSagaStarter), sut.Resolve(typeof(FakeSagaStarter).FullName!));
    }

    [Fact]
    public void Resolve_should_find_every_registered_type_of_an_assembly_by_its_short_name()
    {
        var sut = new TypeResolver();
        sut.Register(typeof(FakeSagaStarter));
        sut.Register(typeof(FakeSagaMessage));

        Assert.Equal(typeof(FakeSagaMessage), sut.Resolve(nameof(FakeSagaMessage)));
    }

    [Fact]
    public async Task Register_and_Resolve_should_be_safe_to_call_concurrently()
    {
        var sut = new TypeResolver();
        var types = typeof(FakeSagaStarter).Assembly.GetTypes().Where(t => t.FullName is not null).ToArray();

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            foreach (var type in types)
            {
                sut.Register(type);
                Assert.Equal(type, sut.Resolve(type.FullName!));
            }
        }));

        await Task.WhenAll(tasks);
    }
}

public class GenericProbe<T> { }
