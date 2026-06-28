# 第三方授權聲明

picture-management 以 MIT 授權釋出,並使用下列第三方套件。各套件授權如下,Apache-2.0 等授權要求的歸屬聲明於此保留。

## ImageSharp 的授權適用

`SixLabors.ImageSharp` 3.x 為雙授權(Apache-2.0 或商業授權)。本專案為開源軟體,符合其適用 Apache License 2.0 的條件,依 Apache-2.0 條款使用。

若你將本專案作為直接相依、用於閉源商用,且為年營收 100 萬美元以上的營利主體,則需另向 Six Labors 取得商業授權,詳見 <https://sixlabors.com/pricing/>。

## DirectML 僅限 Windows / Xbox

`Microsoft.AI.DirectML`(經 `Microsoft.ML.OnnxRuntime.DirectML` 引入)採 Microsoft 軟體授權條款,允許隨應用程式散布,但僅限於 Windows 與 Xbox 平台使用。完整條款見套件內附的 `LICENSE.txt`。Linux/Docker 部署不含 DirectML,推論退回 CPU。

## .NET 套件(NuGet)

| 套件 | 版本 | 授權 |
|---|---|---|
| Magick.NET-Q8-AnyCPU | 14.14.0 | Apache-2.0 |
| Magick.NET.Core | 14.14.0 | Apache-2.0 |
| MetadataExtractor | 2.9.3 | Apache-2.0 |
| Microsoft.AI.DirectML | 1.15.4 | Microsoft Software License(僅限 Windows/Xbox) |
| Microsoft.AspNetCore.OpenApi | 10.0.9 | MIT |
| Microsoft.Data.Sqlite.Core | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore.Abstractions | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore.Analyzers | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore.Relational | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.9 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite.Core | 10.0.9 | MIT |
| Microsoft.Extensions.DependencyModel | 10.0.9 | MIT |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | MIT |
| Microsoft.ML.OnnxRuntime.Managed | 1.24.4 | MIT |
| Microsoft.OpenApi | 2.0.0 | MIT |
| Scalar.AspNetCore | 2.16.5 | MIT |
| Serilog | 4.3.0 | Apache-2.0 |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 |
| Serilog.Formatting.Compact | 3.0.0 | Apache-2.0 |
| Serilog.Settings.Configuration | 10.0.0 | Apache-2.0 |
| Serilog.Sinks.Console | 6.1.1 | Apache-2.0 |
| Serilog.Sinks.Debug | 3.0.0 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |
| SixLabors.ImageSharp | 3.1.12 | Apache-2.0(Six Labors Split License) |
| SourceGear.sqlite3 | 3.50.3 | Public Domain(SQLite 原生庫) |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.1 | Apache-2.0 |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.1 | Apache-2.0 |
| SQLitePCLRaw.core | 3.0.1 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.1 | Apache-2.0 |
| System.Numerics.Tensors | 9.0.0 | MIT |
| XmpCore | 6.1.10.1 | Adobe XMP(BSD-style) |

## 前端套件(npm)

| 套件 | 授權 |
|---|---|
| @angular/* (cdk, common, compiler, core, forms, platform-browser, router) | MIT |
| rxjs | Apache-2.0 |
| tslib | 0BSD |

建置與測試工具(Angular CLI、Tailwind、Vitest、Playwright 等)不隨交付物散布,未列入。

## WD14 標籤模型

WD14 模型(`wd-vit` / `swinv2-tagger-v3`)由 SmilingWolf 釋出,執行時自 Hugging Face 下載,不隨本軟體散布。授權以其 model card 所載為準。
