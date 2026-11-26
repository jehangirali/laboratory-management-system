using System.Security.Cryptography;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LaboratoryManagement.Infrastructure.Persistence;

public sealed class SqliteDatabaseRuntime
{
    private SqliteDatabaseRuntime(string databasePath, string connectionString, byte[] key)
    {
        DatabasePath = databasePath;
        ConnectionString = connectionString;
        ConnectionInitializer = new SqliteConnectionInitializer(key);
    }

    public string DatabasePath { get; }
    public string ConnectionString { get; }
    public SqliteConnectionInitializer ConnectionInitializer { get; }

    public static SqliteDatabaseRuntime Create(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = Environment.ExpandEnvironmentVariables(builder.DataSource);
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || dataSource.Contains('%'))
            throw new InvalidOperationException("The laboratory database must use a local file.");

        var databasePath = Path.GetFullPath(dataSource);
        EnsureLocalPath(databasePath);

        var directory = Path.GetDirectoryName(databasePath)
                        ?? throw new InvalidOperationException("The laboratory database path is invalid.");
        Directory.CreateDirectory(directory);
        builder.DataSource = databasePath;
        var key = SqliteKeyStore.LoadOrCreate(Path.Combine(directory, "db.key"));
        return new SqliteDatabaseRuntime(databasePath, builder.ToString(), key);
    }

    public static void EnsureLocalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            root != null && new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidOperationException("The laboratory database cannot be stored on a network share.");
    }
}

internal static class SqliteKeyStore
{
    public static byte[] LoadOrCreate(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The encrypted laboratory database requires Windows DPAPI.");

        if (!File.Exists(path))
        {
            var key = RandomNumberGenerator.GetBytes(32);
            try
            {
                var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(protectedKey);
                stream.Flush(true);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        var unprotected = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        if (unprotected.Length != 32)
        {
            CryptographicOperations.ZeroMemory(unprotected);
            throw new CryptographicException("The protected database key has an invalid length.");
        }
        return unprotected;
    }
}

public sealed class SqliteConnectionInitializer
{
    private readonly string _keyPragma;

    internal SqliteConnectionInitializer(byte[] key)
        => _keyPragma = $"PRAGMA key = \"x'{Convert.ToHexString(key)}'\";";

    public void Initialize(SqliteConnection connection)
    {
        Execute(connection, _keyPragma);
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=FULL;");
        Execute(connection, "PRAGMA foreign_keys=ON;");
        Execute(connection, "PRAGMA busy_timeout=5000;");
    }

    public async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, _keyPragma, cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA synchronous=FULL;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private readonly SqliteConnectionInitializer _initializer;

    public SqliteConnectionInterceptor(SqliteConnectionInitializer initializer) => _initializer = initializer;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => _initializer.Initialize((SqliteConnection)connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        => _initializer.InitializeAsync((SqliteConnection)connection, cancellationToken);
}
