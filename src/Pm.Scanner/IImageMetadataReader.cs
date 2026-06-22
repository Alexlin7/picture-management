namespace Pm.Scanner;

public interface IImageMetadataReader
{
    /// <summary>只讀。無法解碼/無 EXIF 時對應欄位回 null,不丟例外。</summary>
    ImageMeta Read(string absPath);
}
