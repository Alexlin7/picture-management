using Pm.Api;
using Serilog.Events;
using Xunit;

namespace Pm.Api.Tests;

public class LogLevelsTests
{
    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Critical", LogEventLevel.Fatal)]
    public void Parse_maps_ms_levels(string input, LogEventLevel expected)
    {
        Assert.Equal(expected, LogLevels.Parse(input));
    }

    [Fact]
    public void Parse_None_silences_not_promotes_to_Information()
    {
        // MS 慣例 "None" = 停用該類別。絕不可落到 Information(會反而灌爆 log)。
        var lvl = LogLevels.Parse("None");
        Assert.NotNull(lvl);
        Assert.True(lvl >= LogEventLevel.Fatal, $"None 應被壓到最高(Fatal)以靜默,實際 {lvl}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Warnin")]   // typo
    public void Parse_unrecognized_returns_null_not_Information(string? input)
    {
        // 無法解析的值不可靜默放寬為 Information;回 null 讓呼叫端決定(per-category override 應跳過)。
        Assert.Null(LogLevels.Parse(input));
    }
}
