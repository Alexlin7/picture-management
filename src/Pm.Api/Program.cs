using Pm.Api;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 執行期落點集中解析:dev 維持相對(現狀不動),打包 exe 落 %LOCALAPPDATA%。
// 解析後把絕對路徑寫回既有 config key,讓下方既有 wiring 不動就吃到絕對路徑。
var paths = StoragePaths.Resolve(builder.Environment, builder.Configuration);
Directory.CreateDirectory(paths.BaseDir);   // SQLite 不自建父目錄
Directory.CreateDirectory(paths.LogDir);
builder.Configuration["ConnectionStrings:Pm"] = paths.SqliteDataSource;
builder.Configuration["Thumbnails:Dir"] = paths.ThumbsDir;
builder.Configuration["Inference:Wd14:ModelDir"] = paths.ModelDir;

builder.Host.AddPmSerilog(paths);

builder.Services.AddPmServices(builder.Configuration);

// WD14 自動標籤(opt-in:Inference:Wd14:Enabled,預設關)。開啟才註冊推論工廠 + tagger + 背景 worker。
builder.Services.AddWd14Tagging(builder.Configuration);

// OpenAPI 文件產生(Minimal API 自動掃端點)。供 Scalar UI 與外部工具讀取。
builder.Services.AddOpenApi();

var app = builder.Build();

// schema 確保 + WAL + 孤兒 photo 啟動偵測。
await app.RunStartupTasksAsync();

// 由 .NET serve Angular 靜態檔(ng build 輸出至 wwwroot),同源、免 CORS
app.UseDefaultFiles();
app.UseStaticFiles();

// API 文件:/openapi/v1.json(機器可讀)+ Scalar 互動式 UI 於 /scalar/v1。
// 鐵則 #8(localhost 單人、無認證)下曝露 API explorer 無安全顧慮;日後要收進
// Development-only 只需包一層 if (app.Environment.IsDevelopment())。
app.MapOpenApi();
app.MapScalarApiReference();

// 端點:依領域分檔(Endpoints/*.cs),每組一個 Map*Endpoints extension。
app.MapHealthEndpoints();
app.MapRootEndpoints();
app.MapReconcileEndpoints();
app.MapPathTagEndpoints();
app.MapSearchEndpoints();
app.MapPhotoEndpoints();
app.MapSavedSearchEndpoints();
app.MapBrowseEndpoints();
app.MapTagEndpoints();
app.MapTaggingEndpoints();
app.MapMaintenanceEndpoints();

// SPA fallback:前端路由不被 API 404 攔截
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }   // 供 WebApplicationFactory 測試引用
