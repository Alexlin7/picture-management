using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Api;

// SQLite/EF Core 連線 wiring 與連線字串建構。
public static class SqliteSetup
{
    public static IServiceCollection AddPmDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(new SqliteBusyTimeoutInterceptor(TimeSpan.FromSeconds(5)));
        services.AddDbContext<PmDbContext>((sp, opt) =>
            opt.UseSqlite(BuildConnectionString(config.GetConnectionString("Pm")))
                .AddInterceptors(sp.GetRequiredService<SqliteBusyTimeoutInterceptor>()));
        return services;
    }

    // 鐵則 #10:本專案全 app 的硬刪都靠 DB FK ON DELETE CASCADE(photo→location/photo_tag/tagging_job、
    // tag→photo_tag/tag_relation)。SQLite 因歷史相容預設 foreign_keys=OFF 且為連線層 runtime 設定,
    // 故必須在每條連線強制開啟 —— 此處是唯一真相源(含測試,測試連線字串也流經本函式)。絕不關閉。
    public static string BuildConnectionString(string? configured)
    {
        var cs = string.IsNullOrWhiteSpace(configured) ? "Data Source=pm.sqlite" : configured;
        var builder = new SqliteConnectionStringBuilder(cs)
        {
            DefaultTimeout = 5,
            ForeignKeys = true
        };
        return builder.ToString();
    }
}
