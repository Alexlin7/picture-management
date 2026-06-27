using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;

namespace Pm.Api;

public sealed record RequeueRequestDto(string Mode, RequeueScopeDto Scope);
public sealed record SearchQueryScopeDto(string[]? All = null, string[]? None = null, long? RootId = null, string? PathPrefix = null);
public sealed record RequeueScopeDto(long[]? PhotoIds = null, bool? Error = null, long? Root = null, bool? All = null, SearchQueryScopeDto? Query = null);
public sealed record TaggingScheduleResult(int Matched, int ClearedTags, int JobsCreated, int JobsUpdated);

public sealed class TaggingScheduler(PmDbContext db, PhotoQueryService queryService)
{
    private const int ChunkSize = 10_000;

    public async Task<TaggingScheduleResult> ScheduleAsync(
        string mode,
        RequeueScopeDto? scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedMode is not ("retry" or "refresh" or "clear"))
            throw new ArgumentException("mode must be retry, refresh, or clear", nameof(mode));
        ValidateSingleScope(scope);
        if (normalizedMode == "clear" && scope.Error == true)
            throw new ArgumentException("clear does not support error scope", nameof(scope));

        var photoIds = await ResolvePhotoIdsAsync(scope, ct);
        if (photoIds.Count == 0) return new TaggingScheduleResult(0, 0, 0, 0);

        var cleared = 0;
        if (normalizedMode is "refresh" or "clear")
            cleared = await ClearWd14TagsAsync(photoIds, ct);

        if (normalizedMode == "clear")
            return new TaggingScheduleResult(photoIds.Count, cleared, 0, 0);

        var (created, updated) = await UpsertJobsAsync(photoIds, ct);
        return new TaggingScheduleResult(photoIds.Count, cleared, created, updated);
    }

    private async Task<List<long>> ResolvePhotoIdsAsync(RequeueScopeDto scope, CancellationToken ct)
    {
        if (scope.PhotoIds is { Length: > 0 })
        {
            var requested = scope.PhotoIds.Distinct().ToList();
            var result = new List<long>();
            foreach (var chunk in requested.Chunk(ChunkSize))
            {
                result.AddRange(await db.Photos
                    .Where(p => chunk.Contains(p.Id))
                    .Select(p => p.Id)
                    .ToListAsync(ct));
            }
            return result;
        }

        if (scope.Error == true)
        {
            return await db.TaggingJobs
                .Where(j => j.State == "error")
                .Select(j => j.PhotoId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (scope.Root is { } rootId)
        {
            return await db.PhotoLocations
                .Where(l => l.LibraryRootId == rootId && l.Status == "present")
                .Select(l => l.PhotoId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (scope.All == true)
        {
            return await db.PhotoLocations
                .Where(l => l.Status == "present")
                .Select(l => l.PhotoId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (scope.Query is { } query)
        {
            // 空 all/none 等同「全部 present」(語意同 SearchAsync 無 token)。
            return await queryService.GetAllPhotoIdsAsync(
                query.All ?? Array.Empty<string>(), query.None ?? Array.Empty<string>(),
                query.RootId, query.PathPrefix, ct);
        }

        throw new ArgumentException("scope must include photoIds, error, root, all, or query", nameof(scope));
    }

    private static void ValidateSingleScope(RequeueScopeDto scope)
    {
        var count = 0;
        if (scope.PhotoIds is { Length: > 0 }) count++;
        if (scope.Error == true) count++;
        if (scope.Root is not null) count++;
        if (scope.All == true) count++;
        if (scope.Query is not null) count++;

        if (count != 1)
            throw new ArgumentException("scope must include exactly one of photoIds, error, root, all, or query", nameof(scope));
    }

    private async Task<int> ClearWd14TagsAsync(IReadOnlyCollection<long> photoIds, CancellationToken ct)
    {
        var cleared = 0;
        foreach (var chunk in photoIds.Chunk(ChunkSize))
        {
            cleared += await db.PhotoTags
                .Where(pt => pt.Source == "wd14" && chunk.Contains(pt.PhotoId))
                .ExecuteDeleteAsync(ct);
        }
        return cleared;
    }

    private async Task<(int Created, int Updated)> UpsertJobsAsync(IReadOnlyCollection<long> photoIds, CancellationToken ct)
    {
        var created = 0;
        var updated = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var chunk in photoIds.Chunk(ChunkSize))
        {
            var ids = chunk.ToList();
            var jobs = await db.TaggingJobs
                .Where(j => ids.Contains(j.PhotoId))
                .ToListAsync(ct);
            var existing = jobs.Select(j => j.PhotoId).ToHashSet();

            foreach (var job in jobs)
            {
                job.State = "pending";
                job.Attempts = 0;
                job.UpdatedAt = now;
            }
            updated += jobs.Count;

            foreach (var id in ids)
            {
                if (existing.Contains(id)) continue;
                db.TaggingJobs.Add(new TaggingJob
                {
                    PhotoId = id,
                    State = "pending",
                    Attempts = 0,
                    UpdatedAt = now
                });
                created++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return (created, updated);
    }
}
