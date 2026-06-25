using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Pm.Api;

// 執行期落點集中解析點。BaseDir 規則見 plan Global Constraints。
// 子路徑:config 給相對 → 以 BaseDir 為根;給絕對 → 原樣保留。
public sealed class StoragePaths
{
    public string BaseDir { get; }
    public string SqliteDataSource { get; }
    public string ThumbsDir { get; }
    public string ModelDir { get; }
    public string LogDir { get; }

    private StoragePaths(string baseDir, string sqlite, string thumbs, string modelDir, string logDir)
    {
        BaseDir = baseDir;
        SqliteDataSource = sqlite;
        ThumbsDir = thumbs;
        ModelDir = modelDir;
        LogDir = logDir;
    }

    public static StoragePaths Resolve(IHostEnvironment env, IConfiguration config)
    {
        var baseDir = ResolveBaseDir(env, config);

        var sqliteFile = SqliteFileName(config.GetConnectionString("Pm"));     // 預設 "pm.sqlite"
        var thumbs = config["Thumbnails:Dir"] ?? "thumbs";
        var model = config["Inference:Wd14:ModelDir"] ?? "models/wd14";

        return new StoragePaths(
            baseDir,
            $"Data Source={Combine(baseDir, sqliteFile)}",
            Combine(baseDir, thumbs),
            Combine(baseDir, model),
            Combine(baseDir, "logs"));
    }

    private static string ResolveBaseDir(IHostEnvironment env, IConfiguration config)
    {
        var overridden = config["Storage:BaseDir"];
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);
        if (env.IsProduction())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "sus-picture-management");
        return Directory.GetCurrentDirectory();
    }

    // 相對 → 接在 BaseDir 後並正規化;絕對 → 原樣。
    private static string Combine(string baseDir, string maybeRelative)
    {
        if (Path.IsPathRooted(maybeRelative))
            return maybeRelative;
        var combined = Path.Combine(baseDir, maybeRelative);
        return Path.GetFullPath(combined);
    }

    // 從連線字串拆出 DataSource 檔名(預設 pm.sqlite)。
    private static string SqliteFileName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "pm.sqlite";
        var ds = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(ds) ? "pm.sqlite" : ds;
    }
}
