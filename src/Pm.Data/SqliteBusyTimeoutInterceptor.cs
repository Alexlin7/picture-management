using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pm.Data;

public sealed class SqliteBusyTimeoutInterceptor(TimeSpan timeout) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetBusyTimeout(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetBusyTimeoutAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void SetBusyTimeout(DbConnection connection)
    {
        if (connection is not SqliteConnection) return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout={TimeoutMs(timeout)};";
        cmd.ExecuteNonQuery();
    }

    private async Task SetBusyTimeoutAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection is not SqliteConnection) return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout={TimeoutMs(timeout)};";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static int TimeoutMs(TimeSpan value) =>
        Math.Max(0, (int)Math.Min(int.MaxValue, value.TotalMilliseconds));
}
