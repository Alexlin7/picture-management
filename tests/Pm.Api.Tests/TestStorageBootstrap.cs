using System.Runtime.CompilerServices;

namespace Pm.Api.Tests;

// 整個測試程序啟動前執行一次:把執行期落點導到隔離 temp 目錄,
// 避免 WebApplicationFactory 跑 Program.cs 時污染真實 %LOCALAPPDATA%。
internal static class TestStorageBootstrap
{
    [ModuleInitializer]
    public static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pm-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("Storage__BaseDir", dir);
    }
}
