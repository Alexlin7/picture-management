using Pm.Scanner;

namespace Pm.Api;

public sealed class RootScanCoordinator(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    IHostApplicationLifetime lifetime,
    ILogger<RootScanCoordinator> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<long, RootScanStatus> _statuses = new();

    public RootScanStatus GetStatus(long rootId)
    {
        lock (_gate)
        {
            return _statuses.TryGetValue(rootId, out var status)
                ? status
                : RootScanStatus.Idle(rootId);
        }
    }

    public bool TryStart(long rootId, bool? enqueueTagging, out RootScanStatus status)
    {
        lock (_gate)
        {
            if (_statuses.TryGetValue(rootId, out var existing) && existing.State == "running")
            {
                status = existing;
                return false;
            }

            status = RootScanStatus.Running(rootId);
            _statuses[rootId] = status;
        }

        _ = Task.Run(() => RunScanAsync(rootId, enqueueTagging, lifetime.ApplicationStopping), CancellationToken.None);
        return true;
    }

    private async Task RunScanAsync(long rootId, bool? enqueueTagging, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
            var enqueue = enqueueTagging ?? config.GetValue<bool>("Inference:Wd14:Enabled");
            var result = await scanner.ScanRootAsync(rootId, enqueue, ct);

            Complete(rootId, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Fail(rootId, "scan cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Root scan failed for root {RootId}", rootId);
            Fail(rootId, ex.Message);
        }
    }

    private void Complete(long rootId, ScanResult result)
    {
        lock (_gate)
        {
            _statuses[rootId] = RootScanStatus.Completed(rootId, StartedAtFor(rootId), result);
        }
    }

    private void Fail(long rootId, string error)
    {
        lock (_gate)
        {
            _statuses[rootId] = RootScanStatus.Failed(rootId, StartedAtFor(rootId), error);
        }
    }

    private DateTimeOffset? StartedAtFor(long rootId) =>
        _statuses.TryGetValue(rootId, out var status) ? status.StartedAt : null;
}

public sealed record RootScanStatus(
    long RootId,
    string State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    ScanResult? Result,
    string? Error)
{
    public static RootScanStatus Idle(long rootId) =>
        new(rootId, "idle", null, null, null, null);

    public static RootScanStatus Running(long rootId) =>
        new(rootId, "running", DateTimeOffset.UtcNow, null, null, null);

    public static RootScanStatus Completed(long rootId, DateTimeOffset? startedAt, ScanResult result) =>
        new(rootId, "completed", startedAt, DateTimeOffset.UtcNow, result, null);

    public static RootScanStatus Failed(long rootId, DateTimeOffset? startedAt, string error) =>
        new(rootId, "error", startedAt, DateTimeOffset.UtcNow, null, error);
}
