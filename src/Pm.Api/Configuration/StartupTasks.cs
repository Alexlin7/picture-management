using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Api;

// 啟動任務:確保 schema、開 WAL、孤兒 photo 偵測。app.Run() 前呼叫一次。
public static class StartupTasks
{
    public static async Task RunStartupTasksAsync(this WebApplication app)
    {
        // 啟動時確保 schema 存在(本機單檔,直接 Migrate)
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            await db.Database.MigrateAsync();
            // 開 WAL:API 請求與 WD14 背景 worker 同時寫入 pm.sqlite 時,讀寫不互卡(rollback journal 會)。
            // WAL 為持久設定(寫入 db header),設一次後所有連線生效。短暫寫鎖則靠 connection-level
            // busy_timeout 等待;不能只靠 EF command timeout,因為連線歸還/清理也可能碰到 SQLITE_BUSY。
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        // 啟動偵測:孤兒 photo(零 location)只 log 數量、永不自動刪(清理走 /api/maintenance/orphan-photos)。
        using (var scope = app.Services.CreateScope())
        {
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
                var orphanCount = await db.Photos.CountAsync(p => !p.Locations.Any());
                if (orphanCount > 0)
                    app.Logger.LogInformation(
                        "啟動偵測:孤兒 photo {Count} 筆(零 location;可經 DELETE /api/maintenance/orphan-photos 清理)", orphanCount);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "孤兒 photo 啟動偵測失敗(不影響啟動)");
            }
        }
    }
}
