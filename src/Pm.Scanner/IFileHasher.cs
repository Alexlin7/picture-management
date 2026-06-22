namespace Pm.Scanner;

public interface IFileHasher
{
    /// <summary>串流讀檔算 SHA-256,回 64 字小寫 hex。只讀,不取得寫入控制代碼。</summary>
    Task<string> HashFileAsync(string absPath, CancellationToken ct = default);
}
