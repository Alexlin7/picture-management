using Serilog.Events;

namespace Pm.Api;

/// <summary>
/// appsettings 的 <c>Logging:LogLevel</c> 字串 → Serilog 等級。
/// MS 慣例值對映;<c>"None"</c>(MS = 停用)壓到 Fatal 等同靜默(Serilog 無 off 級);
/// 無法解析(null/空/typo)→ <c>null</c>,由呼叫端決定 —— per-category override 應「跳過」,
/// **不可**靜默放寬為 Information(那會反而調高冗長度,與壓 log 的初衷相反)。
/// </summary>
public static class LogLevels
{
    public static LogEventLevel? Parse(string? level) => level switch
    {
        "Trace" => LogEventLevel.Verbose,
        "Debug" => LogEventLevel.Debug,
        "Information" => LogEventLevel.Information,
        "Warning" => LogEventLevel.Warning,
        "Error" => LogEventLevel.Error,
        "Critical" => LogEventLevel.Fatal,
        "None" => LogEventLevel.Fatal,   // MS "None"=停用;Serilog 無 off,壓到最高等同靜默
        _ => null,
    };
}
