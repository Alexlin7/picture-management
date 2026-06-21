# Phase 1 地基(Foundation)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 立起整個系統的地基 —— 一顆**嵌入式 SQLite**(隨 app 包進去、零安裝)、一個 .NET 10 solution、以 EF Core code-first 落成設計文件 §4.2 的九張表(migration 套得上)、一個能起得來並回報自身與 DB 健康狀態的 ASP.NET Core API,以及 **`IInferenceSessionFactory`**(ONNX Execution Provider 抽象的骨架)。

**Architecture:** B 案「單一 .NET 程序」。整個 app 收成一個行程:ASP.NET Core(API + 日後掃描器/標籤背景服務)+ 嵌入式 SQLite(檔案式、in-process、單程序天然序列化寫入)+ 程序內 ONNX 推論。**不需要 Docker、不需要 Postgres、不需要 Python。** Solution 拆三個專案:`Pm.Data`(EF 實體 + `PmDbContext` + migrations)、`Pm.Ml`(推論 EP 抽象)、`Pm.Api`(宿主,引用前兩者)。**欄位一律 snake_case 顯式映射** —— 讓日後的 recursive CTE 與原生 SQL(§4.4)可直接以 snake_case 名稱存取。

**Tech Stack:** .NET 10.0.301、ASP.NET Core、Entity Framework Core 10.x、`Microsoft.EntityFrameworkCore.Sqlite`、`Microsoft.ML.OnnxRuntime.DirectML`(1.24.x)、xUnit。

## Global Constraints

下列為全專案鐵則(CLAUDE.md「不可違反的鐵則」),每個 task 都隱含適用:

- **絕不修改/搬動/改名原始圖檔,絕不寫 XMP。** 衍生資料(縮圖)放 app 自有快取目錄。(本計畫不碰圖檔,但 schema 反映此原則:`file_hash` 是身分、`file_path` 只是位置。)
- **`file_hash`(SHA-256)是身分,`file_path` 只是位置。** 身分與位置兩層拆開(`photo` ↔ `photo_location`),不得以路徑當主鍵或身分。
- **SQLite 檔是 tag 的唯一真相**(無 XMP)。
- **單一程序**:ML 推論在 .NET 程序內(ONNX in-proc),**不另開程序、不引 broker**;`tagging_job` 表當程序內 DB-backed 佇列。
- **API 只 bind `localhost`**,不做帳號/認證系統(單機單人)。
- **ML 推論經 `IInferenceSessionFactory` 抽象**,預設 DirectML(跨 NVIDIA/AMD),無 GPU 退 CPU;不硬綁 CUDA。
- **欄位命名 snake_case**(對齊 §4.2,讓原生 SQL 可直接存取)。
- **工具鏈版本固定:** .NET SDK `10.0.301`。

---

## File Structure

```
picture-management/
├─ global.json                      # 釘住 SDK 10.0.301
├─ PictureManagement.sln
├─ src/
│  ├─ Pm.Data/
│  │  ├─ Pm.Data.csproj
│  │  ├─ Entities/                  # 九個實體類別,一檔一類
│  │  │  ├─ LibraryRoot.cs
│  │  │  ├─ Photo.cs
│  │  │  ├─ PhotoLocation.cs
│  │  │  ├─ Tag.cs
│  │  │  ├─ TagRelation.cs
│  │  │  ├─ PhotoTag.cs
│  │  │  ├─ PathTagRule.cs
│  │  │  ├─ SavedSearch.cs
│  │  │  └─ TaggingJob.cs
│  │  ├─ PmDbContext.cs             # DbSet + Fluent 設定(SQLite + 顯式 snake_case)
│  │  ├─ PmDbContextFactory.cs      # IDesignTimeDbContextFactory,供 dotnet ef 用
│  │  └─ Migrations/                # dotnet ef 產生
│  ├─ Pm.Ml/
│  │  ├─ Pm.Ml.csproj
│  │  ├─ InferenceBackend.cs        # enum Cpu/DirectMl/Cuda
│  │  ├─ InferenceBackendSelector.cs# 純函式:config/偵測 → backend
│  │  ├─ IInferenceSessionFactory.cs
│  │  ├─ CpuSessionFactory.cs       # 預設 CPU EP
│  │  └─ DirectMlSessionFactory.cs  # AppendExecutionProvider_DML
│  └─ Pm.Api/
│     ├─ Pm.Api.csproj
│     ├─ Program.cs                 # minimal API:/health、/health/db,bind localhost
│     ├─ appsettings.json           # ConnectionStrings:Pm(SQLite)
│     └─ Properties/launchSettings.json
└─ tests/
   ├─ Pm.Data.Tests/
   │  ├─ Pm.Data.Tests.csproj
   │  ├─ ModelTests.cs              # EF 模型映射(不連 DB)
   │  └─ SchemaTests.cs            # 暫存 .sqlite:套 migration + 往返 + 約束
   └─ Pm.Ml.Tests/
      ├─ Pm.Ml.Tests.csproj
      └─ SelectorTests.cs           # EP 選擇邏輯
```

---

## Task 1: 專案骨架(單程序,免 Docker)

立起 solution 與三個專案。本 task 交付物是「`dotnet build` 整個 solution 成功、`dotnet run` 起得來」。**沒有 Docker、沒有外部 DB** —— SQLite 是隨 app 的一個檔。

**Files:**
- Create: `global.json`
- Create: `PictureManagement.sln`
- Create: `src/Pm.Data/Pm.Data.csproj`
- Create: `src/Pm.Ml/Pm.Ml.csproj`
- Create: `src/Pm.Api/Pm.Api.csproj`
- Create: `src/Pm.Api/appsettings.json`
- Create: `src/Pm.Api/Program.cs`(暫時最小,Task 4 補健康檢查)
- Modify: `.gitignore`(加 .NET 產出 + SQLite 檔)

**Interfaces:**
- Consumes: 無(起點)
- Produces: solution `PictureManagement.sln`;連線字串設定鍵 `ConnectionStrings:Pm`(SQLite `Data Source=pm.sqlite`)。

- [ ] **Step 1: 釘住 SDK 版本**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.301",
    "rollForward": "latestPatch"
  }
}
```

- [ ] **Step 2: 建 solution 與三個專案**

Run:

```bash
cd /d/picture-management
dotnet new sln -n PictureManagement
dotnet new classlib -n Pm.Data -o src/Pm.Data
dotnet new classlib -n Pm.Ml -o src/Pm.Ml
dotnet new web -n Pm.Api -o src/Pm.Api
dotnet sln add src/Pm.Data/Pm.Data.csproj src/Pm.Ml/Pm.Ml.csproj src/Pm.Api/Pm.Api.csproj
dotnet add src/Pm.Api/Pm.Api.csproj reference src/Pm.Data/Pm.Data.csproj src/Pm.Ml/Pm.Ml.csproj
rm src/Pm.Data/Class1.cs src/Pm.Ml/Class1.cs
```

- [ ] **Step 3: 裝 Pm.Data 的 EF / SQLite 套件**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Data/Pm.Data.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Pm.Data/Pm.Data.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/Pm.Data/Pm.Data.csproj package Microsoft.EntityFrameworkCore.Sqlite
```

說明:`Microsoft.EntityFrameworkCore.Sqlite` 會抓到 10.x(對齊 EF Core 10),並透過相依帶入 `Microsoft.Data.Sqlite`。

- [ ] **Step 4: 寫連線設定(SQLite 檔)**

Create `src/Pm.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Pm": "Data Source=pm.sqlite"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 5: 暫放最小 Program.cs(Task 4 會擴充)**

Overwrite `src/Pm.Api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Picture Management API");

app.Run();
```

- [ ] **Step 6: 補 .gitignore**

Append to `.gitignore`:

```gitignore
# .NET
bin/
obj/
*.user

# SQLite 本機資料庫(衍生,不入庫)
*.sqlite
*.sqlite-shm
*.sqlite-wal
```

- [ ] **Step 7: 驗證整個 solution 可建置**

Run:

```bash
cd /d/picture-management
dotnet build
```

Expected: `Build succeeded`、0 errors。

- [ ] **Step 8: Commit**

```bash
cd /d/picture-management
git add global.json PictureManagement.sln src/ .gitignore
git commit -m "feat: 單程序專案骨架(Pm.Api/Pm.Data/Pm.Ml/.sln,SQLite,免 Docker)"
```

---

## Task 2: EF Core 九個實體 + PmDbContext(SQLite,顯式 snake_case)

把 §4.2 DDL 落成 EF 實體與 Fluent 設定。SQLite 落地差異:`gps POINT` 拆成 `gps_lat`/`gps_lon`(REAL);`exif`/timestamps 走 TEXT;預設值用 `CURRENT_TIMESTAMP`。本 task 交付物是「`Pm.Data` 編得過、模型結構正確」,以不需 DB 的模型測試把關。

**Files:**
- Create: `src/Pm.Data/Entities/*.cs`(九個)
- Create: `src/Pm.Data/PmDbContext.cs`
- Create: `tests/Pm.Data.Tests/Pm.Data.Tests.csproj`
- Create: `tests/Pm.Data.Tests/ModelTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Pm.Data` 專案。
- Produces:
  - 實體型別(命名空間 `Pm.Data.Entities`):`Photo { long Id; string FileHash; long? FileSize; int? Width; int? Height; string? Mime; DateTimeOffset? TakenAt; string? CameraModel; double? GpsLat; double? GpsLon; string? Exif; DateTimeOffset ImportedAt; }`、`PhotoLocation { long Id; long PhotoId; long LibraryRootId; string RelPath; string Status; DateTimeOffset FirstSeenAt; DateTimeOffset LastSeenAt; }`、`LibraryRoot { long Id; string Name; string AbsPath; DateTimeOffset CreatedAt; }`、`Tag { long Id; string Name; string Kind; }`、`TagRelation { long ParentTagId; long ChildTagId; }`、`PhotoTag { long PhotoId; long TagId; string Source; float? Confidence; }`、`PathTagRule { long Id; long? LibraryRootId; string Segment; string Action; long? TagId; }`、`SavedSearch { long Id; string Name; string QueryJson; DateTimeOffset CreatedAt; }`、`TaggingJob { long PhotoId; string State; int Attempts; DateTimeOffset EnqueuedAt; DateTimeOffset? UpdatedAt; }`
  - `PmDbContext(DbContextOptions<PmDbContext>)`,DbSet:`Photos`、`PhotoLocations`、`LibraryRoots`、`Tags`、`TagRelations`、`PhotoTags`、`PathTagRules`、`SavedSearches`、`TaggingJobs`。表名/欄名全 snake_case。

- [ ] **Step 1: 寫九個實體類別**

Create `src/Pm.Data/Entities/LibraryRoot.cs`:

```csharp
namespace Pm.Data.Entities;

public class LibraryRoot
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string AbsPath { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public List<PhotoLocation> Locations { get; } = new();
}
```

Create `src/Pm.Data/Entities/Photo.cs`:

```csharp
namespace Pm.Data.Entities;

public class Photo
{
    public long Id { get; set; }
    public string FileHash { get; set; } = null!;   // SHA-256 hex,64 字
    public long? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Mime { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? CameraModel { get; set; }
    public double? GpsLat { get; set; }             // SQLite 無 POINT,拆兩欄
    public double? GpsLon { get; set; }
    public string? Exif { get; set; }               // JSON 存 TEXT
    public DateTimeOffset ImportedAt { get; set; }

    public List<PhotoLocation> Locations { get; } = new();
    public List<PhotoTag> Tags { get; } = new();
}
```

Create `src/Pm.Data/Entities/PhotoLocation.cs`:

```csharp
namespace Pm.Data.Entities;

public class PhotoLocation
{
    public long Id { get; set; }
    public long PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;
    public long LibraryRootId { get; set; }
    public LibraryRoot LibraryRoot { get; set; } = null!;
    public string RelPath { get; set; } = null!;
    public string Status { get; set; } = "present";   // present/missing/archived
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
```

Create `src/Pm.Data/Entities/Tag.cs`:

```csharp
namespace Pm.Data.Entities;

public class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;          // booru 式全域唯一名
    public string Kind { get; set; } = "manual";       // path/manual/character/copyright/general/meta
}
```

Create `src/Pm.Data/Entities/TagRelation.cs`:

```csharp
namespace Pm.Data.Entities;

// DAG 邊:一個 tag 可有 0/1/多個上層;不知上游=無此邊,留最上層。
public class TagRelation
{
    public long ParentTagId { get; set; }
    public long ChildTagId { get; set; }
}
```

Create `src/Pm.Data/Entities/PhotoTag.cs`:

```csharp
namespace Pm.Data.Entities;

public class PhotoTag
{
    public long PhotoId { get; set; }
    public long TagId { get; set; }
    public string Source { get; set; } = null!;   // path/manual/wd14
    public float? Confidence { get; set; }
}
```

Create `src/Pm.Data/Entities/PathTagRule.cs`:

```csharp
namespace Pm.Data.Entities;

public class PathTagRule
{
    public long Id { get; set; }
    public long? LibraryRootId { get; set; }      // NULL = 全域
    public string Segment { get; set; } = null!;
    public string Action { get; set; } = null!;   // map_to_tag/ignore/meta_year
    public long? TagId { get; set; }
}
```

Create `src/Pm.Data/Entities/SavedSearch.cs`:

```csharp
namespace Pm.Data.Entities;

public class SavedSearch
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string QueryJson { get; set; } = null!;   // JSON 存 TEXT
    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `src/Pm.Data/Entities/TaggingJob.cs`:

```csharp
namespace Pm.Data.Entities;

public class TaggingJob
{
    public long PhotoId { get; set; }                 // 同時是 PK 與 FK→photo
    public string State { get; set; } = "pending";    // pending/running/done/error
    public int Attempts { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: 寫 PmDbContext(SQLite + snake_case + 約束 + 索引)**

Create `src/Pm.Data/PmDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data.Entities;

namespace Pm.Data;

public class PmDbContext(DbContextOptions<PmDbContext> options) : DbContext(options)
{
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoLocation> PhotoLocations => Set<PhotoLocation>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagRelation> TagRelations => Set<TagRelation>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<PathTagRule> PathTagRules => Set<PathTagRule>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<TaggingJob> TaggingJobs => Set<TaggingJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LibraryRoot>(e =>
        {
            e.ToTable("library_root");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.AbsPath).HasColumnName("abs_path").HasMaxLength(1024).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.AbsPath).IsUnique();
        });

        b.Entity<Photo>(e =>
        {
            e.ToTable("photo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileHash).HasColumnName("file_hash").HasMaxLength(64).IsRequired();
            e.Property(x => x.FileSize).HasColumnName("file_size");
            e.Property(x => x.Width).HasColumnName("width");
            e.Property(x => x.Height).HasColumnName("height");
            e.Property(x => x.Mime).HasColumnName("mime").HasMaxLength(64);
            e.Property(x => x.TakenAt).HasColumnName("taken_at");
            e.Property(x => x.CameraModel).HasColumnName("camera_model").HasMaxLength(128);
            e.Property(x => x.GpsLat).HasColumnName("gps_lat");
            e.Property(x => x.GpsLon).HasColumnName("gps_lon");
            e.Property(x => x.Exif).HasColumnName("exif");
            e.Property(x => x.ImportedAt).HasColumnName("imported_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.FileHash).IsUnique();
            e.HasIndex(x => x.TakenAt).HasDatabaseName("ix_photo_taken");
        });

        b.Entity<PhotoLocation>(e =>
        {
            e.ToTable("photo_location");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PhotoId).HasColumnName("photo_id");
            e.Property(x => x.LibraryRootId).HasColumnName("library_root_id");
            e.Property(x => x.RelPath).HasColumnName("rel_path").HasMaxLength(1024).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("present");
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.Photo).WithMany(p => p.Locations).HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LibraryRoot).WithMany(r => r.Locations).HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.LibraryRootId, x.RelPath }).IsUnique();
            e.HasIndex(x => x.PhotoId).HasDatabaseName("ix_loc_photo");
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("tag");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).HasDefaultValue("manual");
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<TagRelation>(e =>
        {
            e.ToTable("tag_relation", t =>
                t.HasCheckConstraint("ck_tagrel_no_self", "parent_tag_id <> child_tag_id"));
            e.HasKey(x => new { x.ParentTagId, x.ChildTagId });
            e.Property(x => x.ParentTagId).HasColumnName("parent_tag_id");
            e.Property(x => x.ChildTagId).HasColumnName("child_tag_id");
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.ParentTagId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.ChildTagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ChildTagId).HasDatabaseName("ix_tagrel_child");
        });

        b.Entity<PhotoTag>(e =>
        {
            e.ToTable("photo_tag");
            e.HasKey(x => new { x.PhotoId, x.TagId });
            e.Property(x => x.PhotoId).HasColumnName("photo_id");
            e.Property(x => x.TagId).HasColumnName("tag_id");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.HasOne<Photo>().WithMany(p => p.Tags).HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TagId, x.PhotoId }).HasDatabaseName("ix_phototag_tag");
        });

        b.Entity<PathTagRule>(e =>
        {
            e.ToTable("path_tag_rule");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LibraryRootId).HasColumnName("library_root_id");
            e.Property(x => x.Segment).HasColumnName("segment").HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(16).IsRequired();
            e.Property(x => x.TagId).HasColumnName("tag_id");
            e.HasOne<LibraryRoot>().WithMany().HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId);
            e.HasIndex(x => new { x.LibraryRootId, x.Segment }).IsUnique();
        });

        b.Entity<SavedSearch>(e =>
        {
            e.ToTable("saved_search");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.QueryJson).HasColumnName("query_json").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        b.Entity<TaggingJob>(e =>
        {
            e.ToTable("tagging_job");
            e.HasKey(x => x.PhotoId);
            e.Property(x => x.PhotoId).HasColumnName("photo_id").ValueGeneratedNever();
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(16).HasDefaultValue("pending");
            e.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            e.Property(x => x.EnqueuedAt).HasColumnName("enqueued_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Photo>().WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.State).HasDatabaseName("ix_job_state").HasFilter("state IN ('pending','error')");
        });
    }
}
```

- [ ] **Step 3: 建測試專案並接上**

Run:

```bash
cd /d/picture-management
dotnet new xunit -n Pm.Data.Tests -o tests/Pm.Data.Tests
dotnet sln add tests/Pm.Data.Tests/Pm.Data.Tests.csproj
dotnet add tests/Pm.Data.Tests/Pm.Data.Tests.csproj reference src/Pm.Data/Pm.Data.csproj
dotnet add tests/Pm.Data.Tests/Pm.Data.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
```

- [ ] **Step 4: 寫失敗的模型測試**

只查 EF 模型(不連 DB):九個實體都進模型、關鍵表名/欄名為 snake_case、GPS 已拆兩欄。

Create `tests/Pm.Data.Tests/ModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Data.Tests;

public class ModelTests
{
    private static PmDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseSqlite("Data Source=:memory:")   // 只為建出 IModel,不會真的連
            .Options;
        return new PmDbContext(options);
    }

    [Fact]
    public void Model_maps_all_nine_entities()
    {
        using var ctx = BuildContext();
        var model = ctx.Model;

        Assert.NotNull(model.FindEntityType(typeof(LibraryRoot)));
        Assert.NotNull(model.FindEntityType(typeof(Photo)));
        Assert.NotNull(model.FindEntityType(typeof(PhotoLocation)));
        Assert.NotNull(model.FindEntityType(typeof(Tag)));
        Assert.NotNull(model.FindEntityType(typeof(TagRelation)));
        Assert.NotNull(model.FindEntityType(typeof(PhotoTag)));
        Assert.NotNull(model.FindEntityType(typeof(PathTagRule)));
        Assert.NotNull(model.FindEntityType(typeof(SavedSearch)));
        Assert.NotNull(model.FindEntityType(typeof(TaggingJob)));
    }

    [Fact]
    public void Photo_uses_snake_case_and_split_gps()
    {
        using var ctx = BuildContext();
        var photo = ctx.Model.FindEntityType(typeof(Photo))!;

        Assert.Equal("photo", photo.GetTableName());
        Assert.Equal("file_hash", photo.FindProperty(nameof(Photo.FileHash))!.GetColumnName());
        Assert.Equal("camera_model", photo.FindProperty(nameof(Photo.CameraModel))!.GetColumnName());
        Assert.Equal("gps_lat", photo.FindProperty(nameof(Photo.GpsLat))!.GetColumnName());
        Assert.Equal("gps_lon", photo.FindProperty(nameof(Photo.GpsLon))!.GetColumnName());
    }

    [Fact]
    public void PhotoTag_has_composite_primary_key()
    {
        using var ctx = BuildContext();
        var pt = ctx.Model.FindEntityType(typeof(PhotoTag))!;
        var pk = pt.FindPrimaryKey()!;

        Assert.Equal(2, pk.Properties.Count);
        Assert.Contains(pk.Properties, p => p.Name == nameof(PhotoTag.PhotoId));
        Assert.Contains(pk.Properties, p => p.Name == nameof(PhotoTag.TagId));
    }
}
```

- [ ] **Step 5: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Data.Tests/Pm.Data.Tests.csproj
```

Expected: PASS,3 passed。

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Data tests/Pm.Data.Tests
git commit -m "feat: 九個 EF 實體 + PmDbContext(SQLite 顯式 snake_case + 模型測試)"
```

---

## Task 3: 初始 Migration + 暫存 SQLite 整合測試

產出第一份 migration,並用**暫存 `.sqlite` 檔**(免 Docker、免 Testcontainers)套上去,驗證 schema 真的成立:往返寫入、唯一約束、check 約束。本 task 交付物是「migration 套得上、約束生效」。

**Files:**
- Create: `src/Pm.Data/PmDbContextFactory.cs`
- Create: `src/Pm.Data/Migrations/*`(`dotnet ef` 產生)
- Create: `tests/Pm.Data.Tests/SchemaTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `PmDbContext` 與實體。
- Produces: `PmDbContextFactory : IDesignTimeDbContextFactory<PmDbContext>`(設計時用 `Data Source=pm.sqlite`);名為 `InitialSchema` 的 migration。

- [ ] **Step 1: 寫設計時 DbContext 工廠**

Create `src/Pm.Data/PmDbContextFactory.cs`:

```csharp
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
```

- [ ] **Step 2: 裝 dotnet-ef 工具**

Run:

```bash
dotnet tool install --global dotnet-ef
```

說明:若已裝過會印 "already installed",無妨。若 shell 找不到 `dotnet ef`,重開終端或確認 `~/.dotnet/tools` 在 PATH。

- [ ] **Step 3: 產生初始 migration**

Run:

```bash
cd /d/picture-management
dotnet ef migrations add InitialSchema --project src/Pm.Data
```

Expected: 在 `src/Pm.Data/Migrations/` 產生 `*_InitialSchema.cs`,終端印 "Done."。

- [ ] **Step 4: 人工檢查 migration 含關鍵約束**

開啟 `src/Pm.Data/Migrations/*_InitialSchema.cs`,確認以下都在:

- `name: "photo"`、`name: "photo_location"`、`name: "tagging_job"`(snake_case 表名)
- `gps_lat`、`gps_lon` 兩欄(REAL)
- `ck_tagrel_no_self`(check 約束)
- `ix_job_state` 且帶 `filter:`(部分索引)
- `ix_phototag_tag`、`ix_tagrel_child`、`ix_loc_photo`、`ix_photo_taken`

若缺任一,回 Task 2 修正 `PmDbContext` 後重產(先 `dotnet ef migrations remove --project src/Pm.Data`)。

- [ ] **Step 5: 寫失敗的 schema 整合測試**

用暫存檔(每次測試獨立、用完即刪)。SQLite 預設**不強制外鍵**,故連線字串加 `Foreign Keys=True`。

Create `tests/Pm.Data.Tests/SchemaTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Data.Tests;

public class SchemaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-test-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public SchemaTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();   // 套 migration 建出 schema
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // 釋放檔案 handle 才刪得掉
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseSqlite(Cs)
            .Options;
        return new PmDbContext(options);
    }

    [Fact]
    public async Task Round_trip_photo_with_location()
    {
        await using var ctx = NewContext();

        var root = new LibraryRoot { Name = "本機", AbsPath = @"D:\pics" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();

        var photo = new Photo { FileHash = new string('a', 64), FileSize = 1234, Mime = "image/png" };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "vspo/sample.png" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var loaded = await ctx2.Photos
            .Include(p => p.Locations)
            .SingleAsync(p => p.FileHash == new string('a', 64));

        Assert.Equal(1234, loaded.FileSize);
        Assert.Single(loaded.Locations);
        Assert.Equal("present", loaded.Locations[0].Status);   // 預設值生效
    }

    [Fact]
    public async Task Duplicate_file_hash_is_rejected()
    {
        var hash = new string('b', 64);

        await using (var ctx = NewContext())
        {
            ctx.Photos.Add(new Photo { FileHash = hash });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        ctx2.Photos.Add(new Photo { FileHash = hash });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task Tag_relation_self_reference_is_rejected()
    {
        long tagId;
        await using (var ctx = NewContext())
        {
            var t = new Tag { Name = "vspo", Kind = "copyright" };
            ctx.Tags.Add(t);
            await ctx.SaveChangesAsync();
            tagId = t.Id;
        }

        // parent == child 應觸發 ck_tagrel_no_self
        await using var raw = new SqliteConnection(Cs);
        await raw.OpenAsync();
        await using var cmd = raw.CreateCommand();
        cmd.CommandText = "INSERT INTO tag_relation(parent_tag_id, child_tag_id) VALUES ($id, $id)";
        cmd.Parameters.AddWithValue("$id", tagId);

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }
}
```

- [ ] **Step 6: 跑整合測試,確認綠燈**

不需要 Docker、不需要任何外部服務。

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Data.Tests/Pm.Data.Tests.csproj
```

Expected: PASS,6 passed(Task 2 的 3 + 本 task 的 3)。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Data/PmDbContextFactory.cs src/Pm.Data/Migrations tests/Pm.Data.Tests
git commit -m "feat: InitialSchema migration + 暫存 SQLite schema 整合測試"
```

---

## Task 4: API 健康檢查(liveness + DB readiness)+ 綁定 localhost

讓 `Pm.Api` 起得來、回報自身與 DB 健康,並落實「只 bind localhost」。本 task 交付物是「API 跑起來,`/health` 回 200,`/health/db` 在 DB 可開時回 200」。

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Create: `src/Pm.Api/Properties/launchSettings.json`
- Create: `tests/Pm.Api.Tests/Pm.Api.Tests.csproj`
- Create: `tests/Pm.Api.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `PmDbContext`;Task 1 的 `ConnectionStrings:Pm`。
- Produces: `GET /health`(回 `{"status":"ok"}`,200)、`GET /health/db`(DB 可開回 200 `{"db":"ok"}`,否則 503 `{"db":"down"}`)。DI 註冊 `PmDbContext`(SQLite)。Kestrel 僅監聽 `http://localhost:5180`。

- [ ] **Step 1: 在 Api 註冊 DbContext(SQLite)與健康檢查**

Overwrite `src/Pm.Api/Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PmDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Pm")));

var app = builder.Build();

// 啟動時確保 schema 存在(本機單檔,直接 Migrate)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<PmDbContext>().Database.Migrate();
}

// liveness:程序活著就好,不碰 DB
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// readiness:確認 DB 開得起來
app.MapGet("/health/db", async (PmDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { db = "ok" })
        : Results.Json(new { db = "down" }, statusCode: 503);
});

app.MapGet("/", () => "Picture Management API");

app.Run();

public partial class Program { }   // 供 WebApplicationFactory 測試引用
```

- [ ] **Step 2: 釘住 localhost 監聽位址(鐵則:不對外)**

Create `src/Pm.Api/Properties/launchSettings.json`:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "Pm.Api": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5180",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

說明:`localhost` 而非 `0.0.0.0` —— 對齊鐵則。日後要走 NAS/多人(spec §11),改這裡的同時必須補認證。

- [ ] **Step 3: 建 API 測試專案**

Run:

```bash
cd /d/picture-management
dotnet new xunit -n Pm.Api.Tests -o tests/Pm.Api.Tests
dotnet sln add tests/Pm.Api.Tests/Pm.Api.Tests.csproj
dotnet add tests/Pm.Api.Tests/Pm.Api.Tests.csproj reference src/Pm.Api/Pm.Api.csproj
dotnet add tests/Pm.Api.Tests/Pm.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 4: 寫失敗的 liveness 測試**

Create `tests/Pm.Api.Tests/HealthTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pm.Api.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    [Fact]
    public async Task HealthDb_returns_ok()
    {
        // WebApplicationFactory 啟動時會 Migrate 出本機 pm.sqlite,DB 可開
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/db");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("\"db\":\"ok\"", await resp.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 5: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj
```

Expected: PASS,2 passed。

- [ ] **Step 6: 手動煙霧測試(免 DB 預備,SQLite 自動建檔)**

Run:

```bash
cd /d/picture-management
dotnet run --project src/Pm.Api &
sleep 4
curl -s http://localhost:5180/health
curl -s http://localhost:5180/health/db
kill %1
```

Expected: 分別回 `{"status":"ok"}` 與 `{"db":"ok"}`;工作目錄出現 `pm.sqlite`。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: API 健康檢查(/health + /health/db,SQLite)+ 綁定 localhost"
```

---

## Task 5: `IInferenceSessionFactory` 骨架(EP 抽象 + 選擇邏輯)

把「ONNX 推論落在哪個 Execution Provider」抽象掉的地基。本 task **不跑真模型**,只立起介面、兩個實作(CPU / DirectML),以及一個**純函式選擇器**(依啟動參數或偵測到的顯卡決定 backend)並測試它。把「DirectML 維護模式」風險關進這層,日後加 CUDA 只是多一個 publish profile(見 spec §7 GPU/EP)。

**Files:**
- Create: `src/Pm.Ml/InferenceBackend.cs`
- Create: `src/Pm.Ml/InferenceBackendSelector.cs`
- Create: `src/Pm.Ml/IInferenceSessionFactory.cs`
- Create: `src/Pm.Ml/CpuSessionFactory.cs`
- Create: `src/Pm.Ml/DirectMlSessionFactory.cs`
- Create: `tests/Pm.Ml.Tests/Pm.Ml.Tests.csproj`
- Create: `tests/Pm.Ml.Tests/SelectorTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Pm.Ml` 專案。
- Produces:
  - `enum InferenceBackend { Cpu, DirectMl, Cuda }`
  - `InferenceBackendSelector.Select(string? configured, string? gpuVendor) -> InferenceBackend`(純函式)
  - `IInferenceSessionFactory { InferenceBackend Backend { get; } InferenceSession Create(string modelPath); }`
  - `CpuSessionFactory`、`DirectMlSessionFactory`(實作上述介面;`Create` 將於 Phase 1 後段 WD14 計畫實際使用)

- [ ] **Step 1: 裝 ONNX Runtime DirectML 套件**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Ml/Pm.Ml.csproj package Microsoft.ML.OnnxRuntime.DirectML
```

說明:此套件同時帶 CPU EP 與 DirectML EP(`AppendExecutionProvider_DML`),涵蓋 NVIDIA/AMD;版本抓 1.24.x。

- [ ] **Step 2: 寫 backend enum 與選擇器**

Create `src/Pm.Ml/InferenceBackend.cs`:

```csharp
namespace Pm.Ml;

public enum InferenceBackend
{
    Cpu,
    DirectMl,
    Cuda
}
```

Create `src/Pm.Ml/InferenceBackendSelector.cs`:

```csharp
namespace Pm.Ml;

// 純函式:啟動參數優先,否則依偵測到的顯卡。
// 本 build 只帶 DirectML(+CPU);CUDA 僅於專屬 publish profile 才可用,
// 故偵測到 GPU 一律回 DirectMl,Cuda 只能由 configured 明示。
public static class InferenceBackendSelector
{
    public static InferenceBackend Select(string? configured, string? gpuVendor)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant() switch
            {
                "cpu"  => InferenceBackend.Cpu,
                "dml" or "directml" => InferenceBackend.DirectMl,
                "cuda" => InferenceBackend.Cuda,
                _ => throw new ArgumentException($"未知的推論 backend:'{configured}'")
            };
        }

        // 沒指定 → 有顯卡走 DirectML(跨 NV/AMD),沒有就 CPU。
        return string.IsNullOrWhiteSpace(gpuVendor)
            ? InferenceBackend.Cpu
            : InferenceBackend.DirectMl;
    }
}
```

- [ ] **Step 3: 寫介面與兩個實作**

Create `src/Pm.Ml/IInferenceSessionFactory.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public interface IInferenceSessionFactory
{
    InferenceBackend Backend { get; }
    InferenceSession Create(string modelPath);
}
```

Create `src/Pm.Ml/CpuSessionFactory.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class CpuSessionFactory : IInferenceSessionFactory
{
    public InferenceBackend Backend => InferenceBackend.Cpu;

    public InferenceSession Create(string modelPath) => new(modelPath);   // 預設 CPU EP
}
```

Create `src/Pm.Ml/DirectMlSessionFactory.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class DirectMlSessionFactory : IInferenceSessionFactory
{
    private readonly int _deviceId;
    public DirectMlSessionFactory(int deviceId = 0) => _deviceId = deviceId;

    public InferenceBackend Backend => InferenceBackend.DirectMl;

    public InferenceSession Create(string modelPath)
    {
        var so = new SessionOptions();
        so.AppendExecutionProvider_DML(_deviceId);
        return new InferenceSession(modelPath, so);
    }
}
```

- [ ] **Step 4: 建 Pm.Ml 測試專案**

Run:

```bash
cd /d/picture-management
dotnet new xunit -n Pm.Ml.Tests -o tests/Pm.Ml.Tests
dotnet sln add tests/Pm.Ml.Tests/Pm.Ml.Tests.csproj
dotnet add tests/Pm.Ml.Tests/Pm.Ml.Tests.csproj reference src/Pm.Ml/Pm.Ml.csproj
```

- [ ] **Step 5: 寫失敗的選擇器測試**

只測純函式選擇邏輯(不建 session、不需模型)。

Create `tests/Pm.Ml.Tests/SelectorTests.cs`:

```csharp
using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class SelectorTests
{
    [Theory]
    [InlineData("cpu", InferenceBackend.Cpu)]
    [InlineData("dml", InferenceBackend.DirectMl)]
    [InlineData("directml", InferenceBackend.DirectMl)]
    [InlineData("cuda", InferenceBackend.Cuda)]
    public void Configured_param_wins(string configured, InferenceBackend expected)
    {
        Assert.Equal(expected, InferenceBackendSelector.Select(configured, gpuVendor: "NVIDIA"));
    }

    [Fact]
    public void No_config_with_gpu_picks_directml()
    {
        Assert.Equal(InferenceBackend.DirectMl,
            InferenceBackendSelector.Select(configured: null, gpuVendor: "AMD Radeon"));
    }

    [Fact]
    public void No_config_no_gpu_falls_back_to_cpu()
    {
        Assert.Equal(InferenceBackend.Cpu,
            InferenceBackendSelector.Select(configured: null, gpuVendor: null));
    }

    [Fact]
    public void Unknown_configured_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InferenceBackendSelector.Select("metal", gpuVendor: null));
    }
}
```

- [ ] **Step 6: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Ml.Tests/Pm.Ml.Tests.csproj
```

Expected: PASS,7 passed(4 個 Theory case + 3 個 Fact)。

- [ ] **Step 7: 全 solution 總驗收**

Run:

```bash
cd /d/picture-management
dotnet test
```

Expected: 全綠 —— `Pm.Data.Tests`(6)+ `Pm.Api.Tests`(2)+ `Pm.Ml.Tests`(7)= 15 passed。

- [ ] **Step 8: Commit**

```bash
cd /d/picture-management
git add src/Pm.Ml tests/Pm.Ml.Tests
git commit -m "feat: IInferenceSessionFactory 骨架(EP 抽象 + CPU/DirectML 實作 + 選擇器測試)"
```

---

## 完成定義(地基)

跑完五個 task 後應同時成立:

- `dotnet build` 整個 solution 成功;**全程不需 Docker / Postgres / Python**。
- `dotnet test` 全綠(15 passed):模型映射、schema 往返、唯一/check 約束、API liveness/readiness、EP 選擇邏輯。
- `dotnet run --project src/Pm.Api` 起得來,首次自動建 `pm.sqlite`,`/health`、`/health/db` 皆回 200。
- SQLite 內九張表、所有索引與約束均依 §4.2 成立(GPS 為 `gps_lat`/`gps_lon`),欄位皆 snake_case。
- API 僅監聽 `localhost`。
- `IInferenceSessionFactory` 抽象就位,backend 可由參數/偵測選擇,CPU/DirectML 兩實作可用。

這份地基為後續計畫備妥:`PmDbContext` 與實體、可用的 SQLite、可加背景服務的 `Pm.Api` 宿主、以及 WD14 計畫要接的推論抽象。

---

## Self-Review 註記

- **Spec 覆蓋:** 對應 §2(SQLite + ONNX in-proc 技術棧)、§3(單程序 + `IInferenceSessionFactory`)、§4.2(九表 DDL,GPS 拆兩欄、SQLite 型別)、§4.3 身分/位置兩層、§7 決策(單程序收斂、儲存引擎、GPU/EP)。掃描/查詢/標籤資料流(§5)屬後續計畫。
- **無 placeholder:** 所有 step 含可直接執行的指令或完整程式碼。
- **型別一致:** DbSet 名稱、實體屬性(`GpsLat`/`GpsLon`、`FileHash`)、`IInferenceSessionFactory` 簽章在各 Task 的 Interfaces 與測試程式中一致。
- **B 案落實:** 無 Docker/Postgres/Testcontainers/Python;測試以暫存 `.sqlite` 與純函式為主。
