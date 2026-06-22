namespace Pm.Scanner;

public interface IThumbnailService
{
    /// <summary>依 hash 分桶的縮圖檔路徑(不保證已存在)。</summary>
    string PathFor(string hash);

    /// <summary>產縮圖到 PathFor(hash)。成功回路徑,失敗回 null。只讀原圖。</summary>
    Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default);
}
