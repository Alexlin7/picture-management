using Microsoft.Data.Sqlite;
using Pm.Api;
using Xunit;

namespace Pm.Api.Tests;

// 鐵則 #10:全 app 硬刪靠 DB FK ON DELETE CASCADE,SQLite 預設 foreign_keys=OFF 且為連線層
// runtime 設定。SqliteSetup.BuildConnectionString 是唯一真相源,故在此鎖住「永遠開 FK」這個不變式。
public class SqliteSetupTests
{
    [Fact]
    public void BuildConnectionString_永遠開啟ForeignKeys()
    {
        var cs = SqliteSetup.BuildConnectionString("Data Source=foo.sqlite");
        var b = new SqliteConnectionStringBuilder(cs);
        Assert.True(b.ForeignKeys ?? false, "連線字串必須帶 ForeignKeys=True(鐵則 #10)");
    }

    [Fact]
    public void BuildConnectionString_空輸入回退預設檔名且開FK()
    {
        var cs = SqliteSetup.BuildConnectionString(null);
        var b = new SqliteConnectionStringBuilder(cs);
        Assert.Equal("pm.sqlite", b.DataSource);
        Assert.True(b.ForeignKeys ?? false);
    }

    [Fact]
    public void BuildConnectionString_保留呼叫者DataSource並設DefaultTimeout()
    {
        var cs = SqliteSetup.BuildConnectionString("Data Source=/tmp/x.sqlite");
        var b = new SqliteConnectionStringBuilder(cs);
        Assert.Equal("/tmp/x.sqlite", b.DataSource);
        Assert.Equal(5, b.DefaultTimeout);
    }
}
