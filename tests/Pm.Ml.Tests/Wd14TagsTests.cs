using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class Wd14TagsTests
{
    [Fact]
    public void Parses_csv_skipping_header()
    {
        var lines = new[]
        {
            "tag_id,name,category,count",
            "1,1girl,0,1000000",
            "2,hakurei_reimu,4,50000",
            "3,general,9,999",
        };
        var tags = Wd14Tags.Parse(lines);
        Assert.Equal(3, tags.Count);
        Assert.Equal("1girl", tags[0].Name);
        Assert.Equal(0, tags[0].Category);
        Assert.Equal(4, tags[1].Category);
    }
}
