namespace Pm.Scanner;

/// <summary>一次掃描的統計。</summary>
public sealed record ScanResult(
    int FilesSeen,         // 看到的圖片檔總數
    int NewPhotos,         // 新身分(新 hash)
    int NewLocations,      // 新位置(新的 root+rel_path)
    int SkippedUnchanged,  // 快路徑跳過(size+mtime 沒變)
    int Errors);           // 讀取失敗略過
