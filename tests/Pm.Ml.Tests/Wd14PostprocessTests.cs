using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class Wd14PostprocessTests
{
    [Theory]
    [InlineData(0, "general")]
    [InlineData(4, "character")]
    [InlineData(3, "copyright")]
    [InlineData(9, "meta")]
    [InlineData(7, "general")]
    public void KindOf_maps_category(int cat, string kind) => Assert.Equal(kind, Wd14Postprocess.KindOf(cat));

    [Fact]
    public void Select_applies_per_category_thresholds_and_skips_rating()
    {
        var tags = new List<Wd14Tag>
        {
            new(1, "1girl", 0),            // general
            new(2, "reimu", 4),            // character
            new(3, "low_conf_char", 4),    // character,信心不足
            new(4, "explicit", 9),         // rating → 跳過
        };
        var probs = new[] { 0.9f, 0.9f, 0.5f, 0.99f };

        var selected = Wd14Postprocess.Select(probs, tags, generalThreshold: 0.35f, characterThreshold: 0.85f);

        Assert.Contains(selected, s => s.Name == "1girl" && s.Kind == "general");
        Assert.Contains(selected, s => s.Name == "reimu" && s.Kind == "character");
        Assert.DoesNotContain(selected, s => s.Name == "low_conf_char");   // 0.5 < 0.85
        Assert.DoesNotContain(selected, s => s.Name == "explicit");        // rating 跳過
    }
}
