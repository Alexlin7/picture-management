using Pm.Scanner;

namespace Pm.Api;

// Composition root:應用服務 DI 註冊(DbContext 走 SqliteSetup.AddPmDatabase,
// WD14 自動標籤走 Wd14Setup.AddWd14Tagging — 兩者由 Program.cs 各自呼叫)。
public static class ServiceRegistration
{
    public static IServiceCollection AddPmServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddPmDatabase(config);

        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection("Thumbnails").Get<ThumbnailOptions>()
                ?? new ThumbnailOptions());
        services.AddScoped<IFileHasher, Sha256FileHasher>();
        services.AddScoped<IImageMetadataReader, ExifImageMetadataReader>();
        services.AddScoped<IThumbnailService, ThumbnailService>();
        services.AddScoped<LibraryScanner>();
        services.AddScoped<PathTagService>();
        services.AddScoped<TagClosureService>();
        services.AddScoped<PhotoQueryService>();
        services.AddScoped<TagFacetService>();
        services.AddScoped<FolderTreeService>();
        services.AddScoped<TagService>();
        services.AddScoped<CopyrightAxisService>();
        services.AddScoped<TaggingScheduler>();
        services.AddSingleton<RootScanCoordinator>();
        return services;
    }
}
