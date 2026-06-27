namespace Pm.Scanner;
using Pm.Data.Entities;

public sealed record ReprocessResult(bool Decoded, bool ThumbGenerated);

public interface IImageReprocessor
{
    Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct = default);
}
