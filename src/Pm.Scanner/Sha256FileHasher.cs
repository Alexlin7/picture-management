using System.Security.Cryptography;

namespace Pm.Scanner;

public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<string> HashFileAsync(string absPath, CancellationToken ct = default)
    {
        // FileShare.Read:允許他人同時讀;FileAccess.Read:我們絕不寫原檔。
        await using var fs = new FileStream(
            absPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexStringLower(hash);   // .NET 9+ 內建小寫 hex
    }
}
