using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Utils;

public class StringExtensionsTests
{
    [Fact]
    public void ComputeSha256Hash_should_return_64_char_hex_string()
    {
        var hash = "hello".ComputeSha256Hash();

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeSha256Hash_should_return_same_hash_for_same_input()
    {
        var hash1 = "test-data".ComputeSha256Hash();
        var hash2 = "test-data".ComputeSha256Hash();

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hash_should_return_different_hash_for_different_input()
    {
        var hash1 = "input1".ComputeSha256Hash();
        var hash2 = "input2".ComputeSha256Hash();

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hash_should_produce_known_hash_for_empty_string()
    {
        var hash = "".ComputeSha256Hash();
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }
}
