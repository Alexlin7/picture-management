using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

// IThumbnailService stub:GenerateAsync 一律拋例外(模擬損毀但標頭可讀的圖在全幅 Load 時失敗)。
file sealed class ThrowingThumbnailService : IThumbnailService
{
    public string PathFor(string hash) => string.Empty;
    public Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated GenerateAsync failure");
}

public class ImageReprocessorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-rep-{Guid.NewGuid():N}");
    public ImageReprocessorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string WriteRealPng(string name, int w = 4, int h = 2)
    {
        var path = Path.Combine(_dir, name);
        using var img = new Image<Rgba32>(w, h);
        img.SaveAsPng(path);
        return path;
    }

    private ImageReprocessor MakeSut(out string thumbDir)
    {
        thumbDir = Path.Combine(_dir, "thumbs");
        var thumbs = new ThumbnailService(new ThumbnailOptions { Dir = thumbDir, MaxEdge = 64 });
        return new ImageReprocessor(new ExifImageMetadataReader(), thumbs);
    }

    [Fact]
    public async Task Decodable_image_fills_metadata_and_generates_thumb()
    {
        var path = WriteRealPng("a.png", 4, 2);
        var sut = MakeSut(out var thumbDir);
        var photo = new Photo { FileHash = "ab" + new string('0', 62) };

        var result = await sut.ReprocessAsync(photo, path);

        Assert.True(result.Decoded);
        Assert.True(result.ThumbGenerated);
        Assert.Equal(4, photo.Width);
        Assert.Equal(2, photo.Height);
        Assert.Equal("image/png", photo.Mime);
        Assert.True(File.Exists(Path.Combine(thumbDir, "ab", "00", photo.FileHash + ".webp")));
    }

    [Fact]
    public async Task Undecodable_file_reports_not_decoded_and_no_thumb()
    {
        var path = Path.Combine(_dir, "bad.png");
        File.WriteAllText(path, "not an image");
        var sut = MakeSut(out var thumbDir);
        var photo = new Photo { FileHash = "cd" + new string('0', 62) };

        var result = await sut.ReprocessAsync(photo, path);

        Assert.False(result.Decoded);
        Assert.False(result.ThumbGenerated);
        Assert.Null(photo.Width);
        Assert.False(File.Exists(Path.Combine(thumbDir, "cd", "00", photo.FileHash + ".webp")));
    }

    // I1:GenerateAsync 拋例外時不應往上傳播 —— metadata 已補回應回報 Decoded=true、
    // ThumbGenerated=false,且 photo.Width 仍被寫入(對齊 spec §6)。
    [Fact]
    public async Task GenerateAsync_exception_does_not_propagate_and_metadata_is_written()
    {
        var path = WriteRealPng("ok.png", 8, 4);
        var sut = new ImageReprocessor(new ExifImageMetadataReader(), new ThrowingThumbnailService());
        var photo = new Photo { FileHash = "ef" + new string('0', 62) };

        var result = await sut.ReprocessAsync(photo, path);   // must NOT throw

        Assert.True(result.Decoded);
        Assert.False(result.ThumbGenerated);
        Assert.Equal(8, photo.Width);   // metadata written back despite thumb failure
        Assert.Equal(4, photo.Height);
    }
}
