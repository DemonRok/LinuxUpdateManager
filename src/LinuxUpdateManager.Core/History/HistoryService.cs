using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LinuxUpdateManager.Core.History;

public sealed class HistoryService
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumOutputLength = 250_000;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public HistoryService(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinuxUpdateManager",
            "history.db");
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath)
                ?? throw new InvalidOperationException("The history database path has no parent directory.");
            Directory.CreateDirectory(directory);

            await using var connection = await OpenConnectionAsync(cancellationToken);
            var schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidDataException("The operation history database was created by a newer application version.");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS operations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    server_id TEXT NOT NULL,
                    server_name TEXT NOT NULL,
                    server_host TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    ended_utc TEXT NOT NULL,
                    result TEXT NOT NULL,
                    exit_status INTEGER NULL,
                    sanitized_output TEXT NOT NULL,
                    error_summary TEXT NULL,
                    package_count INTEGER NULL,
                    security_update_count INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS ix_operations_started_utc ON operations(started_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_operations_server_started ON operations(server_id, started_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_operations_type_result ON operations(operation_type, result);
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task RecordAsync(OperationHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(record.OperationType) ||
            string.IsNullOrWhiteSpace(record.Result) || record.EndedAtUtc < record.StartedAtUtc)
        {
            throw new ArgumentException("The operation history record is invalid.", nameof(record));
        }

        await InitializeAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO operations (
                    server_id, server_name, server_host, operation_type, started_utc, ended_utc,
                    result, exit_status, sanitized_output, error_summary, package_count, security_update_count)
                VALUES (
                    $serverId, $serverName, $serverHost, $operationType, $startedUtc, $endedUtc,
                    $result, $exitStatus, $output, $errorSummary, $packageCount, $securityUpdateCount);
                """;
            command.Parameters.AddWithValue("$serverId", record.ServerId.ToString("D"));
            command.Parameters.AddWithValue("$serverName", Limit(record.ServerName, 256));
            command.Parameters.AddWithValue("$serverHost", Limit(record.ServerHost, 512));
            command.Parameters.AddWithValue("$operationType", Limit(record.OperationType, 64));
            command.Parameters.AddWithValue("$startedUtc", ToDatabaseTime(record.StartedAtUtc));
            command.Parameters.AddWithValue("$endedUtc", ToDatabaseTime(record.EndedAtUtc));
            command.Parameters.AddWithValue("$result", Limit(record.Result, 32));
            command.Parameters.AddWithValue("$exitStatus", (object?)record.ExitStatus ?? DBNull.Value);
            command.Parameters.AddWithValue("$output", Limit(record.SanitizedOutput, MaximumOutputLength));
            command.Parameters.AddWithValue("$errorSummary", (object?)LimitNullable(record.ErrorSummary, 4_000) ?? DBNull.Value);
            command.Parameters.AddWithValue("$packageCount", (object?)record.PackageCount ?? DBNull.Value);
            command.Parameters.AddWithValue("$securityUpdateCount", (object?)record.SecurityUpdateCount ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationHistoryEntry>> SearchAsync(
        OperationHistoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        filter ??= new OperationHistoryFilter();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            clauses.Add("(instr(lower(server_name), lower($search)) > 0 OR instr(lower(server_host), lower($search)) > 0 OR instr(lower(operation_type), lower($search)) > 0 OR instr(lower(result), lower($search)) > 0 OR instr(lower(coalesce(error_summary, '')), lower($search)) > 0 OR instr(lower(sanitized_output), lower($search)) > 0)");
            command.Parameters.AddWithValue("$search", filter.SearchText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.OperationType))
        {
            clauses.Add("operation_type = $operationType");
            command.Parameters.AddWithValue("$operationType", filter.OperationType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Result))
        {
            clauses.Add("result = $result");
            command.Parameters.AddWithValue("$result", filter.Result);
        }

        if (filter.StartedAfterUtc is { } after)
        {
            clauses.Add("started_utc >= $startedAfter");
            command.Parameters.AddWithValue("$startedAfter", ToDatabaseTime(after));
        }

        if (filter.StartedBeforeUtc is { } before)
        {
            clauses.Add("started_utc < $startedBefore");
            command.Parameters.AddWithValue("$startedBefore", ToDatabaseTime(before));
        }

        var where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
        command.CommandText = $"""
            SELECT id, server_id, server_name, server_host, operation_type, started_utc, ended_utc,
                   result, exit_status, sanitized_output, error_summary, package_count, security_update_count
            FROM operations {where}
            ORDER BY started_utc DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(filter.MaximumEntries, 1, 10_000));

        var entries = new List<OperationHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new OperationHistoryEntry(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12)));
        }

        return entries;
    }

    public async Task<OperationHistoryEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var matches = await SearchAsync(new OperationHistoryFilter(MaximumEntries: 10_000), cancellationToken);
        return matches.FirstOrDefault(entry => entry.Id == id);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM operations WHERE ended_utc < $cutoff";
            command.Parameters.AddWithValue("$cutoff", ToDatabaseTime(cutoffUtc));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static string ToDatabaseTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string? LimitNullable(string? value, int maximum) =>
        value is null ? null : Limit(value, maximum);
}
