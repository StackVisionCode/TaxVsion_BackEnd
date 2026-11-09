using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace TaxVision.BuildingBlocks.Testing.Integration;

/// <summary>
/// BB-10. Base temporal de las T-INT de un servicio: la flota no la lee, así que una corrida abortada no le deja filas
/// ni sobres. Una por proceso, con nombre único: dos corridas a la vez no se pisan. Las clases que la comparten usan
/// cada una su tenant, igual que antes en la base local, que solo ubica el servidor.
///
/// <para>La borra <see cref="TemporaryDatabaseTestFramework"/> al terminar la corrida (<c>ProcessExit</c> no alcanza:
/// el runner mata el proceso a media limpieza). La de una corrida abortada la borra la siguiente, cuando ya es vieja
/// y nadie está conectado.</para>
/// </summary>
public sealed class TemporaryTestDatabase
{
    /// <summary>Más que el tiempo entre crear la base y abrirle la primera conexión.</summary>
    public static readonly TimeSpan DefaultOrphanAge = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, string> Created = new();

    private readonly string _prefix;
    private readonly Func<string> _serverConnectionString;
    private readonly Action<string> _createSchema;
    private readonly TimeSpan _orphanAge;
    private readonly Lazy<string> _connectionString;

    /// <param name="prefix">Solo minúsculas, dígitos y <c>_</c>; identifica las bases de este servicio.</param>
    /// <param name="serverConnectionString">La base local; se lee al usar la base por primera vez.</param>
    /// <param name="createSchema">Crea el esquema en la cadena que recibe.</param>
    /// <param name="orphanAge">Desde qué edad se borra una base sin conexiones del mismo prefijo.</param>
    public TemporaryTestDatabase(
        string prefix,
        Func<string> serverConnectionString,
        Action<string> createSchema,
        TimeSpan? orphanAge = null
    )
    {
        if (
            string.IsNullOrEmpty(prefix)
            || !prefix.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
        )
            throw new ArgumentException("Use lowercase letters, digits and '_'.", nameof(prefix));

        _prefix = prefix;
        _serverConnectionString = serverConnectionString;
        _createSchema = createSchema;
        _orphanAge = orphanAge ?? DefaultOrphanAge;
        _connectionString = new Lazy<string>(Create);
    }

    public string ConnectionString => _connectionString.Value;

    /// <summary>Para las pruebas que crean sus propias tablas.</summary>
    public static void CreateEmpty(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        using var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database}];";
        command.ExecuteNonQuery();
    }

    /// <summary>Borra las bases que creó este proceso.</summary>
    public static void DropCreated()
    {
        SqlConnection.ClearAllPools();
        foreach (var (name, server) in Created)
        {
            DropWhere(server, $"name = N'{name}'");
            Created.TryRemove(name, out _);
        }
    }

    private string Create()
    {
        var server = _serverConnectionString();
        DropOrphans(server);

        var name = $"{_prefix}_{Guid.NewGuid():N}"[..(_prefix.Length + 13)];
        Created[name] = server;
        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        _createSchema(connectionString);
        return connectionString;
    }

    private void DropOrphans(string server) =>
        DropWhere(
            server,
            $"""
            name LIKE N'{_prefix.Replace("_", "[_]")}[_]%'
              AND DATEDIFF(MINUTE, create_date, GETDATE()) >= {(int)_orphanAge.TotalMinutes}
              AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_sessions s WHERE s.database_id = sys.databases.database_id)
            """
        );

    private static void DropWhere(string server, string filter)
    {
        var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString;
        using var connection = new SqlConnection(master);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE {filter};
            EXEC sp_executesql @sql;
            """;
        command.ExecuteNonQuery();
    }
}
