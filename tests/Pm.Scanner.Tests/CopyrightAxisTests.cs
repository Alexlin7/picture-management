using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class CopyrightAxisTests
{
    [Theory]
    [InlineData("aris_(blue_archive)", "blue_archive")]       // 單作品
    [InlineData("jeanne_d'arc_(alter)_(fate)", "fate")]       // 最右非黑名單=作品,alter 為造型
    [InlineData("hoshino_(blue_archive)", "blue_archive")]
    public void Extracts_rightmost_non_blacklist_group_as_work(string name, string work)
        => Assert.Equal(work, CopyrightAxis.ParseWork(name));

    [Theory]
    [InlineData("long_hair")]              // 無括號
    [InlineData("aris_(cosplay)")]         // 全黑名單(cosplay)
    [InlineData("someone_(male)")]         // 全黑名單(male)
    [InlineData("_(foo)")]                 // 畸形:括號前無名
    [InlineData("")]                       // 空
    public void Returns_null_when_no_work(string name)
        => Assert.Null(CopyrightAxis.ParseWork(name));
}
