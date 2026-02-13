namespace OpenSleigh.Tests;

public class SystemInfoTests
{
    [Fact]
    public void Ctor_should_throw_when_clientId_is_null()
    {
        Assert.Throws<ArgumentException>(() => new SystemInfo("group", null!));
    }

    [Fact]
    public void Ctor_should_throw_when_clientId_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new SystemInfo("group", ""));
    }

    [Fact]
    public void Ctor_should_throw_when_clientId_is_whitespace()
    {
        Assert.Throws<ArgumentException>(() => new SystemInfo("group", "  "));
    }

    [Fact]
    public void Ctor_should_throw_when_clientGroup_is_null()
    {
        Assert.Throws<ArgumentException>(() => new SystemInfo(null!, "client-1"));
    }

    [Fact]
    public void Ctor_should_throw_when_clientGroup_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new SystemInfo("", "client-1"));
    }

    [Fact]
    public void Ctor_should_set_properties()
    {
        var sut = new SystemInfo("my-group", "client-1");

        Assert.Equal("my-group", sut.ClientGroup);
        Assert.Equal("client-1", sut.ClientId);
        Assert.False(sut.PublishOnly);
    }

    [Fact]
    public void Create_should_generate_ids_without_configuration()
    {
        var sut = SystemInfo.Create();

        Assert.NotNull(sut.ClientId);
        Assert.NotEmpty(sut.ClientId);
        Assert.NotNull(sut.ClientGroup);
        Assert.NotEmpty(sut.ClientGroup);
    }

    [Fact]
    public void Id_should_combine_group_and_clientId()
    {
        ISystemInfo sut = new SystemInfo("group", "client");

        Assert.Equal("group.client", sut.Id);
    }
}
