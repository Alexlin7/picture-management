using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Pm.Scanner;

public sealed class ThumbnailService(ThumbnailOptions options) : IThumbnailService
{
    public string PathFor(string hash) =>
        Path.Combine(options.Dir, hash[..2], hash[2..4], hash + ".webp");

    public async Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default)
    {
        var outPath = PathFor(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using var img = await Image.LoadAsync(absPath, ct);   // 只讀原圖
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,                            // 保持比例,長邊不超過 MaxEdge
            Size = new Size(options.MaxEdge, options.MaxEdge),
        }));
        await img.SaveAsWebpAsync(outPath, ct);
        return outPath;
    }
}
