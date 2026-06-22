using System.Text;
using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class Wd14ModelProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-dl-{Guid.NewGuid():N}");

    public Wd14ModelProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Download_writes_dest_and_leaves_no_part_on_success()
    {
        var dest = Path.Combine(_dir, "model.onnx");
        var payload = Encoding.UTF8.GetBytes("onnx-bytes");

        await Wd14ModelProvider.DownloadAsync(_ => Task.FromResult<Stream>(new MemoryStream(payload)), dest, default);

        Assert.True(File.Exists(dest));
        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));   // 暫存檔已 rename 掉
    }

    [Fact]
    public async Task Download_interrupted_leaves_no_dest_and_cleans_part()
    {
        var dest = Path.Combine(_dir, "model.onnx");

        // 串流讀到一半就拋(模擬斷線/逾時),dest 不該留半截壞檔。
        await Assert.ThrowsAsync<IOException>(() =>
            Wd14ModelProvider.DownloadAsync(_ => Task.FromResult<Stream>(new ThrowingStream()), dest, default));

        Assert.False(File.Exists(dest));            // 下次 File.Exists==false → 會重抓
        Assert.False(File.Exists(dest + ".part"));  // 半截暫存檔已清掉
    }

    // 一被讀取就拋的 stream。
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("boom");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => throw new IOException("boom");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
