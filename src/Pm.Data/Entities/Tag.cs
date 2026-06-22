namespace Pm.Data.Entities;

public class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;          // booru 式全域唯一名(保留顯示拼寫)
    // 不分大小寫去重鍵 = Name.ToLowerInvariant()(全 Unicode;SQLite 內建 lower() 只折 ASCII 不夠)。
    // 由 PmDbContext.SaveChanges 自動維護,呼叫端只需設 Name。
    public string NameCi { get; set; } = "";
    public string Kind { get; set; } = "manual";       // path/manual/character/copyright/general/meta
}
