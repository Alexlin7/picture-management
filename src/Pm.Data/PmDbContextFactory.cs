using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pm.Data;

// 僅供 dotnet ef 設計時建模/產 migration 使用,執行期不走這條。
public class PmDbContextFactory : IDesignTimeDbContextFactory<PmDbContext>
{
    public PmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseSqlite("Data Source=pm.sqlite")
            .Options;
        return new PmDbContext(options);
    }
}
