using Serilog;
using Serilog.Events;

namespace Pm.Api;

// Serilog wiring:console(dev 看得到)+ rolling file(落 logs/)。
// 注意:UseSerilog 會繞過 MS Logging:LogLevel 過濾,故 MinimumLevel 必須在此明設。
// log 級別全是行為層 knob → 一律走 appsettings 的 Logging:LogLevel(改 json/env + 重啟即生效,免重編、不硬編):
//   Default → 全域下限;其餘 key(Microsoft.AspNetCore / Microsoft.EntityFrameworkCore…)→ per-category override。
//   例:把 EF 的 "Executed DbCommand"(Information 級 SQL,tagging_job 每 4s 灌爆 log)壓成 Warning,就在 appsettings 設。
public static class SerilogSetup
{
    public static IHostBuilder AddPmSerilog(this IHostBuilder host, StoragePaths paths)
    {
        host.UseSerilog((context, logCfg) =>
        {
            logCfg.MinimumLevel.Is(LogLevels.Parse(context.Configuration["Logging:LogLevel:Default"]) ?? LogEventLevel.Information);
            foreach (var entry in context.Configuration.GetSection("Logging:LogLevel").GetChildren())
            {
                if (entry.Key == "Default") continue;
                var lvl = LogLevels.Parse(entry.Value);
                if (lvl is not null) logCfg.MinimumLevel.Override(entry.Key, lvl.Value);   // 無法解析 → 跳過,不靜默放寬為 Information
            }
            logCfg
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(paths.LogDir, "pm-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 50L * 1024 * 1024,
                    rollOnFileSizeLimit: true);
        });
        return host;
    }
}
