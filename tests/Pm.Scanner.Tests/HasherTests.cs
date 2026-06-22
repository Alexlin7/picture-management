using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class HasherTests
{
    [Fact]
    public async Task Hashes_known_vector_abc()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pm-hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, "abc"u8.ToArray());
        try
        {
            var hash = await new Sha256FileHasher().HashFileAsync(path);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
            Assert.Equal(64, hash.Length);
        }
        finally { File.Delete(path); }
    }
}
