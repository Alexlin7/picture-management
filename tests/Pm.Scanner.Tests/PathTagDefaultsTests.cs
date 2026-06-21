using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PathTagDefaultsTests
{
    [Theory]
    [InlineData("我不知道", "ignore")]
    [InlineData("2024", "meta_year")]
    [InlineData("2023", "meta_year")]
    [InlineData("vspo", "map_to_tag")]
    [InlineData("12", "map_to_tag")]      // 非四位數不算年份
    public void Suggest_maps_segment_to_action(string segment, string expected)
        => Assert.Equal(expected, PathTagDefaults.Suggest(segment));
}
