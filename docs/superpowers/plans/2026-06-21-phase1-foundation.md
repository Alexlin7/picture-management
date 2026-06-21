# Phase 1 地基(Foundation)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 立起整個系統的地基 —— 一顆可連的 Postgres、一個 .NET 10 solution、以 EF Core code-first 落成設計文件 §4.2 的九張表(migration 套得上真 DB),以及一個能起得來、能回報自身與 DB 健康狀態的 ASP.NET Core API。

**Architecture:** Postgres 走 Docker(`pgvector/pgvector:pg17`,Phase 2 才用得到 pgvector,先用同一顆 image 免日後換)。.NET solution 拆兩個專案:`Pm.Data`(EF 實體 + `PmDbContext` + migrations,class library)與 `Pm.Api`(ASP.NET Core minimal API,引用 `Pm.Data`)。整合測試用 Testcontainers 起一顆即拋的 Postgres,把 migration 套上去驗證 schema 正確。**欄位一律 snake_case 顯式映射** —— 因為日後 Python worker 與 §4.4 的 recursive CTE 會直接以 snake_case 名稱打 DB,EF 預設的 PascalCase 欄名會讓那些原生 SQL 失聯。

**Tech Stack:** .NET 10.0.301、ASP.NET Core、Entity Framework Core 10.x、Npgsql.EntityFrameworkCore.PostgreSQL 10.x、xUnit、Testcontainers.PostgreSql、Docker(`pgvector/pgvector:pg17`)。

## Global Constraints

下列為全專案鐵則(CLAUDE.md「不可違反的鐵則」),每個 task 都隱含適用:

- **絕不修改/搬動/改名原始圖檔,絕不寫 XMP。** 衍生資料(縮圖)放 app 自有快取目錄。(本計畫不碰圖檔,但 schema 設計反映此原則:`file_hash` 是身分、`file_path` 只是位置。)
- **`file_hash`(SHA-256,CHAR(64))是身分,`file_path` 只是位置。** 身分與位置兩層拆開(`photo` ↔ `photo_location`),不得以路徑當主鍵或身分。
- **DB 是 tag 的唯一真相**(無 XMP)。
- **API 只 bind `localhost`**,不做帳號/認證系統(單機單人)。
- **.NET ↔ Python 經 `tagging_job` 表(DB-as-queue)**,不引入 broker。
- **欄位命名 snake_case**(對齊 §4.2 DDL,讓原生 SQL 與 Python worker 可直接存取)。
- **工具鏈版本固定:** .NET SDK `10.0.301`、Postgres image `pgvector/pgvector:pg17`。

---

## File Structure

```
picture-management/
├─ global.json                      # 釘住 SDK 10.0.301
├─ docker-compose.yml               # postgres 服務(pgvector/pgvector:pg17)
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
│  │  ├─ PmDbContext.cs             # DbSet + Fluent 設定(顯式 snake_case)
│  │  ├─ PmDbContextFactory.cs      # IDesignTimeDbContextFactory,供 dotnet ef 用
│  │  └─ Migrations/                # dotnet ef 產生
│  └─ Pm.Api/
│     ├─ Pm.Api.csproj
│     ├─ Program.cs                 # minimal API:/health、/health/db,bind localhost
│     └─ appsettings.json           # ConnectionStrings:Pm
└─ tests/
   └─ Pm.Data.Tests/
      ├─ Pm.Data.Tests.csproj
      └─ SchemaTests.cs             # Testcontainers:套 migration + 往返 + 約束驗證
```

---

## Task 1: 專案骨架 + Postgres 開發容器

立起 solution、兩個專案、開發用 Postgres 容器與 SDK 釘版。本 task 交付物是「`docker compose up -d` 後 DB 健康、`dotnet build` 整個 solution 成功」。

**Files:**
- Create: `global.json`
- Create: `docker-compose.yml`
- Create: `PictureManagement.sln`
- Create: `src/Pm.Data/Pm.Data.csproj`
- Create: `src/Pm.Api/Pm.Api.csproj`
- Create: `src/Pm.Api/appsettings.json`
- Create: `src/Pm.Api/Program.cs`(暫時最小,Task 4 補健康檢查)
- Modify: `.gitignore`(加 .NET 產出)

**Interfaces:**
- Consumes: 無(起點)
- Produces: solution `PictureManagement.sln`;連線字串設定鍵 `ConnectionStrings:Pm`;DB 服務名 `postgres`、DB 名 `picturemanagement`、使用者 `pm`。

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

- [ ] **Step 2: 寫 Postgres 開發容器**

Create `docker-compose.yml`:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg17
    container_name: pm-pg
    environment:
      POSTGRES_USER: pm
      POSTGRES_PASSWORD: pm_dev_pw
      POSTGRES_DB: picturemanagement
    ports:
      - "5432:5432"
    volumes:
      - pm-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U pm -d picturemanagement"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  pm-pgdata:
```

- [ ] **Step 3: 建 solution 與兩個專案**

Run:

```bash
cd /d/picture-management
dotnet new sln -n PictureManagement
dotnet new classlib -n Pm.Data -o src/Pm.Data
dotnet new web -n Pm.Api -o src/Pm.Api
dotnet sln add src/Pm.Data/Pm.Data.csproj src/Pm.Api/Pm.Api.csproj
dotnet add src/Pm.Api/Pm.Api.csproj reference src/Pm.Data/Pm.Data.csproj
rm src/Pm.Data/Class1.cs
```

- [ ] **Step 4: 裝 Pm.Data 的 EF 套件**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Data/Pm.Data.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Pm.Data/Pm.Data.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/Pm.Data/Pm.Data.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

說明:`Npgsql.EntityFrameworkCore.PostgreSQL` 會抓到 10.x(對齊 .NET 10)。`Design` 套件供 `dotnet ef` 設計時建模。

- [ ] **Step 5: 寫連線設定**

Create `src/Pm.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Pm": "Host=localhost;Port=5432;Database=picturemanagement;Username=pm;Password=pm_dev_pw"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 6: 暫放最小 Program.cs(Task 4 會擴充)**

Overwrite `src/Pm.Api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Picture Management API");

app.Run();
```

- [ ] **Step 7: 補 .gitignore**

Append to `.gitignore`:

```gitignore
# .NET
bin/
obj/
*.user
```

- [ ] **Step 8: 起 DB 並驗證健康**

Run:

```bash
cd /d/picture-management
docker compose up -d postgres
docker inspect --format='{{.State.Health.Status}}' pm-pg
```

Expected: 約 5–15 秒後印出 `healthy`(若還是 `starting` 等幾秒重跑 inspect)。

- [ ] **Step 9: 驗證整個 solution 可建置**

Run:

```bash
cd /d/picture-management
dotnet build
```

Expected: `Build succeeded`、0 errors。

- [ ] **Step 10: Commit**

```bash
cd /d/picture-management
git add global.json docker-compose.yml PictureManagement.sln src/ .gitignore
git commit -m "feat: 專案骨架 + Postgres 開發容器(Pm.Api/Pm.Data/.sln/compose)"
```

---

## Task 2: EF Core 九個實體 + PmDbContext(顯式 snake_case 映射)

把 §4.2 DDL 落成 EF 實體與 Fluent 設定。本 task 不碰 DB,交付物是「`Pm.Data` 編得過、模型結構正確」,以一支不需 DB 的模型單元測試把關。

**Files:**
- Create: `src/Pm.Data/Entities/LibraryRoot.cs`、`Photo.cs`、`PhotoLocation.cs`、`Tag.cs`、`TagRelation.cs`、`PhotoTag.cs`、`PathTagRule.cs`、`SavedSearch.cs`、`TaggingJob.cs`
- Create: `src/Pm.Data/PmDbContext.cs`
- Create: `tests/Pm.Data.Tests/Pm.Data.Tests.csproj`
- Create: `tests/Pm.Data.Tests/ModelTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Pm.Data` 專案。
- Produces:
  - 實體型別(命名空間 `Pm.Data.Entities`):`Photo { long Id; string FileHash; long? FileSize; int? Width; int? Height; string? Mime; DateTimeOffset? TakenAt; string? CameraModel; NpgsqlPoint? Gps; string? Exif; DateTimeOffset ImportedAt; }`、`PhotoLocation { long Id; long PhotoId; long LibraryRootId; string RelPath; string Status; DateTimeOffset FirstSeenAt; DateTimeOffset LastSeenAt; }`、`LibraryRoot { long Id; string Name; string AbsPath; DateTimeOffset CreatedAt; }`、`Tag { long Id; string Name; string Kind; }`、`TagRelation { long ParentTagId; long ChildTagId; }`、`PhotoTag { long PhotoId; long TagId; string Source; float? Confidence; }`、`PathTagRule { long Id; long? LibraryRootId; string Segment; string Action; long? TagId; }`、`SavedSearch { long Id; string Name; string QueryJson; DateTimeOffset CreatedAt; }`、`TaggingJob { long PhotoId; string State; int Attempts; DateTimeOffset EnqueuedAt; DateTimeOffset? UpdatedAt; }`
  - `PmDbContext(DbContextOptions<PmDbContext>)`,含 DbSet:`Photos`、`PhotoLocations`、`LibraryRoots`、`Tags`、`TagRelations`、`PhotoTags`、`PathTagRules`、`SavedSearches`、`TaggingJobs`。表名/欄名全 snake_case。

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
using NpgsqlTypes;

namespace Pm.Data.Entities;

public class Photo
{
    public long Id { get; set; }
    public string FileHash { get; set; } = null!;   // SHA-256 hex,CHAR(64)
    public long? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Mime { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? CameraModel { get; set; }
    public NpgsqlPoint? Gps { get; set; }
    public string? Exif { get; set; }               // jsonb
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
    public string QueryJson { get; set; } = null!;   // jsonb
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

- [ ] **Step 2: 寫 PmDbContext(顯式 snake_case + 約束 + 索引)**

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
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasIndex(x => x.AbsPath).IsUnique();
        });

        b.Entity<Photo>(e =>
        {
            e.ToTable("photo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileHash).HasColumnName("file_hash").HasColumnType("char(64)").IsRequired();
            e.Property(x => x.FileSize).HasColumnName("file_size");
            e.Property(x => x.Width).HasColumnName("width");
            e.Property(x => x.Height).HasColumnName("height");
            e.Property(x => x.Mime).HasColumnName("mime").HasMaxLength(64);
            e.Property(x => x.TakenAt).HasColumnName("taken_at");
            e.Property(x => x.CameraModel).HasColumnName("camera_model").HasMaxLength(128);
            e.Property(x => x.Gps).HasColumnName("gps").HasColumnType("point");
            e.Property(x => x.Exif).HasColumnName("exif").HasColumnType("jsonb");
            e.Property(x => x.ImportedAt).HasColumnName("imported_at").HasDefaultValueSql("now()");
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
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("now()");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("now()");
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
            e.Property(x => x.QueryJson).HasColumnName("query_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        b.Entity<TaggingJob>(e =>
        {
            e.ToTable("tagging_job");
            e.HasKey(x => x.PhotoId);
            e.Property(x => x.PhotoId).HasColumnName("photo_id").ValueGeneratedNever();
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(16).HasDefaultValue("pending");
            e.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            e.Property(x => x.EnqueuedAt).HasColumnName("enqueued_at").HasDefaultValueSql("now()");
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
dotnet add tests/Pm.Data.Tests/Pm.Data.Tests.csproj package Microsoft.EntityFrameworkCore
```

- [ ] **Step 4: 寫失敗的模型測試**

這支測試只查 EF 模型(不連 DB):驗證九個實體都進了模型、關鍵表名/欄名為 snake_case。

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
        // 只為了讓 OnModelCreating 跑起來建出 IModel,連線字串不會真的去連。
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
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
    public void Photo_uses_snake_case_table_and_columns()
    {
        using var ctx = BuildContext();
        var photo = ctx.Model.FindEntityType(typeof(Photo))!;

        Assert.Equal("photo", photo.GetTableName());

        var hash = photo.FindProperty(nameof(Photo.FileHash))!;
        Assert.Equal("file_hash", hash.GetColumnName());

        var cam = photo.FindProperty(nameof(Photo.CameraModel))!;
        Assert.Equal("camera_model", cam.GetColumnName());
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

- [ ] **Step 5: 跑測試,確認先失敗(編譯期)**

此時尚未裝 `Microsoft.EntityFrameworkCore.Design`/Npgsql 進測試專案,但測試引用 `UseNpgsql`,需要 Npgsql 套件。先跑一次確認紅燈:

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Data.Tests/Pm.Data.Tests.csproj
```

Expected: FAIL —— 編譯錯誤 `UseNpgsql` 找不到(缺 Npgsql 套件參考)。

- [ ] **Step 6: 補測試專案的 Npgsql 參考**

Run:

```bash
cd /d/picture-management
dotnet add tests/Pm.Data.Tests/Pm.Data.Tests.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

- [ ] **Step 7: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Data.Tests/Pm.Data.Tests.csproj
```

Expected: PASS,3 passed。

- [ ] **Step 8: Commit**

```bash
cd /d/picture-management
git add src/Pm.Data tests/Pm.Data.Tests
git commit -m "feat: 九個 EF 實體 + PmDbContext(顯式 snake_case 映射 + 模型測試)"
```

---

## Task 3: 初始 Migration + Testcontainers 整合測試

產出第一份 migration,並用 Testcontainers 起一顆即拋 Postgres、把 migration 套上去,驗證 schema 真的成立:往返寫入、唯一約束、check 約束都如預期。本 task 交付物是「migration 套得上真 DB 且約束生效」。

**Files:**
- Create: `src/Pm.Data/PmDbContextFactory.cs`
- Create: `src/Pm.Data/Migrations/*`（`dotnet ef` 產生)
- Create: `tests/Pm.Data.Tests/SchemaTests.cs`
- Modify: `tests/Pm.Data.Tests/Pm.Data.Tests.csproj`(加 Testcontainers 套件)

**Interfaces:**
- Consumes: Task 2 的 `PmDbContext` 與實體。
- Produces: `PmDbContextFactory : IDesignTimeDbContextFactory<PmDbContext>`(設計時用 localhost 連線字串);名為 `InitialSchema` 的 migration。

- [ ] **Step 1: 寫設計時 DbContext 工廠**

`dotnet ef` 設計時需要能建出 `PmDbContext`。在 `Pm.Data` 自帶工廠,免依賴 `Pm.Api` 啟動流程。

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
            .UseNpgsql("Host=localhost;Port=5432;Database=picturemanagement;Username=pm;Password=pm_dev_pw")
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

Expected: 在 `src/Pm.Data/Migrations/` 產生 `*_InitialSchema.cs` 等檔,終端印 "Done."。

- [ ] **Step 4: 人工檢查 migration 含關鍵約束**

開啟 `src/Pm.Data/Migrations/*_InitialSchema.cs`,確認以下字串都在(肉眼即可):

- `table: "photo"`、`table: "photo_location"`、`table: "tagging_job"`(snake_case 表名)
- `ck_tagrel_no_self`(check 約束)
- `ix_job_state` 且帶 `filter:`(部分索引)
- `ix_phototag_tag`、`ix_tagrel_child`、`ix_loc_photo`、`ix_photo_taken`

若缺任一,回 Task 2 對照修正 `PmDbContext` 後重產(先 `dotnet ef migrations remove --project src/Pm.Data`)。

- [ ] **Step 5: 裝 Testcontainers**

Run:

```bash
cd /d/picture-management
dotnet add tests/Pm.Data.Tests/Pm.Data.Tests.csproj package Testcontainers.PostgreSql
```

- [ ] **Step 6: 寫失敗的 schema 整合測試**

Create `tests/Pm.Data.Tests/SchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pm.Data;
using Pm.Data.Entities;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pm.Data.Tests;

public class SchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    private string _cs = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _cs = _pg.GetConnectionString();

        // 把 migration 套上去
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    private PmDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseNpgsql(_cs)
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

        var loaded = await ctx.Photos
            .Include(p => p.Locations)
            .SingleAsync(p => p.FileHash == new string('a', 64));

        Assert.Equal(1234, loaded.FileSize);
        Assert.Single(loaded.Locations);
        Assert.Equal("present", loaded.Locations[0].Status);   // 預設值生效
    }

    [Fact]
    public async Task Duplicate_file_hash_is_rejected()
    {
        await using var ctx = NewContext();
        var hash = new string('b', 64);

        ctx.Photos.Add(new Photo { FileHash = hash });
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        ctx2.Photos.Add(new Photo { FileHash = hash });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task Tag_relation_self_reference_is_rejected()
    {
        await using var ctx = NewContext();
        var t = new Tag { Name = "vspo", Kind = "copyright" };
        ctx.Tags.Add(t);
        await ctx.SaveChangesAsync();

        // parent == child 應觸發 ck_tagrel_no_self
        await using var raw = new NpgsqlConnection(_cs);
        await raw.OpenAsync();
        await using var cmd = raw.CreateCommand();
        cmd.CommandText = "INSERT INTO tag_relation(parent_tag_id, child_tag_id) VALUES (@id, @id)";
        cmd.Parameters.AddWithValue("id", t.Id);

        await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }
}
```

- [ ] **Step 7: 跑整合測試,確認綠燈**

需要 Docker 在跑(Testcontainers 會自行拉 image、起容器、用完即拋)。

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Data.Tests/Pm.Data.Tests.csproj
```

Expected: PASS,全部 passed(含 Task 2 的 3 個 + 本 task 的 3 個 = 6)。首次會花時間拉 `pgvector/pgvector:pg17` image。

- [ ] **Step 8: Commit**

```bash
cd /d/picture-management
git add src/Pm.Data/PmDbContextFactory.cs src/Pm.Data/Migrations tests/Pm.Data.Tests
git commit -m "feat: InitialSchema migration + Testcontainers schema 整合測試"
```

---

## Task 4: API 健康檢查(liveness + DB readiness)+ 綁定 localhost

讓 `Pm.Api` 起得來、回報自身與 DB 健康,並落實鐵則「只 bind localhost」。本 task 交付物是「API 跑起來,`/health` 回 200,`/health/db` 在 DB 通時回 200」。

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Modify: `src/Pm.Api/Pm.Api.csproj`(加 EF 參考已於 Task 1 透過專案引用具備;此處加健康檢查套件)
- Create: `src/Pm.Api/Properties/launchSettings.json`(釘 localhost url)
- Create: `tests/Pm.Api.Tests/Pm.Api.Tests.csproj`
- Create: `tests/Pm.Api.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `PmDbContext`;Task 1 的 `ConnectionStrings:Pm`。
- Produces: HTTP 端點 `GET /health`(回 `{"status":"ok"}`,200)、`GET /health/db`(DB 可連回 200 `{"db":"ok"}`,不可連回 503 `{"db":"down"}`)。DI 註冊 `PmDbContext`。Kestrel 僅監聽 `http://localhost:5180`。

- [ ] **Step 1: 在 Api 註冊 DbContext 與健康檢查**

Overwrite `src/Pm.Api/Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PmDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Pm")));

var app = builder.Build();

// liveness:程序活著就好,不碰 DB
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// readiness:確認 DB 連得上
app.MapGet("/health/db", async (PmDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { db = "ok" })
        : Results.Json(new { db = "down" }, statusCode: 503);
});

app.MapGet("/", () => "Picture Management API");

app.Run();
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

說明:`localhost` 而非 `0.0.0.0` —— 對齊鐵則「API 只 bind localhost」。日後若要走 NAS/多人(spec §11),改這裡的同時必須補認證。

- [ ] **Step 3: 建 API 測試專案**

Run:

```bash
cd /d/picture-management
dotnet new xunit -n Pm.Api.Tests -o tests/Pm.Api.Tests
dotnet sln add tests/Pm.Api.Tests/Pm.Api.Tests.csproj
dotnet add tests/Pm.Api.Tests/Pm.Api.Tests.csproj reference src/Pm.Api/Pm.Api.csproj
dotnet add tests/Pm.Api.Tests/Pm.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 4: 讓 Pm.Api 可被測試專案當函式庫引用**

`WebApplicationFactory<Program>` 需要 `Program` 類別可見。在 `Program.cs` 末端加一行 partial 宣告。

Append to `src/Pm.Api/Program.cs`:

```csharp

public partial class Program { }
```

- [ ] **Step 5: 寫失敗的 liveness 測試**

`/health` 不碰 DB,可在無 DB 環境通過。

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
}
```

- [ ] **Step 6: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj
```

Expected: PASS,1 passed。

- [ ] **Step 7: 手動煙霧測試 /health/db(需 DB 在跑)**

Run:

```bash
cd /d/picture-management
docker compose up -d postgres
dotnet run --project src/Pm.Api &
sleep 4
curl -s http://localhost:5180/health
curl -s http://localhost:5180/health/db
```

Expected: 第一個回 `{"status":"ok"}`、第二個回 `{"db":"ok"}`。

驗證後關掉背景的 API:

```bash
kill %1
```

- [ ] **Step 8: 跑整個 solution 的測試做總驗收**

Run:

```bash
cd /d/picture-management
dotnet test
```

Expected: 全綠 —— `Pm.Data.Tests`(6)+ `Pm.Api.Tests`(1)= 7 passed。

- [ ] **Step 9: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: API 健康檢查(/health + /health/db)+ 綁定 localhost"
```

---

## 完成定義(地基)

跑完四個 task 後應同時成立:

- `docker compose up -d postgres` → `pm-pg` 健康。
- `dotnet test` 全綠(7 passed):模型映射、schema 往返、唯一/check 約束、API liveness。
- `dotnet run --project src/Pm.Api` 起得來,`/health`、`/health/db` 皆回 200。
- DB 內九張表、所有索引與約束均依 §4.2 成立,欄位皆 snake_case。
- API 僅監聽 `localhost`。

這份地基為**計畫 2(Scanner 身分與位置)**備妥:`PmDbContext`、`Photo`/`PhotoLocation`/`LibraryRoot` 實體、可連的 DB、可加背景服務的 `Pm.Api` 宿主。

---

## Self-Review 註記

- **Spec 覆蓋:** 本計畫對應 spec §4.2(九表 DDL,全數落成)、§4.3 身分/位置兩層(`photo`↔`photo_location` FK)、鐵則「localhost only」「snake_case 可被原生 SQL 存取」「DB-as-queue 之 `tagging_job` 表」。掃描/查詢/標籤等資料流(§5)屬後續計畫,不在地基範圍。
- **無 placeholder:** 所有 step 含可直接執行的指令或完整程式碼。
- **型別一致:** `PmDbContext` 的 DbSet 名稱、實體屬性名與型別,在 Task 2 Interfaces 與後續 Task 的測試程式中一致(`Photos`、`FileHash`、`PhotoLocations` 等)。
