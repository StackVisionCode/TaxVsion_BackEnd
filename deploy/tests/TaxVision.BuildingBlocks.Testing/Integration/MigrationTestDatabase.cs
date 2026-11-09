using Microsoft.Data.SqlClient;

namespace TaxVision.BuildingBlocks.Testing.Integration;

/// <summary>
/// Base vacía y propia para probar una migración con filas del esquema anterior: la prueba migra hasta justo antes,
/// siembra con <see cref="InsertAsync"/> y migra el resto. Se borra al terminar.
/// </summary>
public sealed class MigrationTestDatabase : IAsyncDisposable
{
    /// <param name="serverConnectionString">La base local; solo ubica el servidor.</param>
    /// <param name="prefix">Solo minúsculas, dígitos y <c>_</c>.</param>
    public MigrationTestDatabase(string serverConnectionString, string prefix)
    {
        ConnectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 13)],
        }.ConnectionString;
        TemporaryTestDatabase.CreateEmpty(ConnectionString);
    }

    public string ConnectionString { get; }

    /// <summary>Inserta una fila; las columnas obligatorias sin default que no vienen en <paramref name="values"/> reciben un valor neutro.</summary>
    public async Task InsertAsync(string table, IReadOnlyDictionary<string, object?> values)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var row = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var column in await RequiredColumnsAsync(connection, table))
            if (!row.ContainsKey(column.Name))
                row[column.Name] = Neutral(column.Type, column.Length);

        await using var command = connection.CreateCommand();
        var names = row.Keys.ToList();
        command.CommandText =
            $"INSERT INTO [{table}] ({string.Join(", ", names.Select(n => $"[{n}]"))}) "
            + $"VALUES ({string.Join(", ", names.Select((_, i) => $"@p{i}"))});";
        for (var i = 0; i < names.Count; i++)
            command.Parameters.AddWithValue($"@p{i}", row[names[i]] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync(), typeof(T))!;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        var name = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<(string Name, string Type, int Length)>> RequiredColumnsAsync(
        SqlConnection connection,
        string table
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, t.name, c.max_length
            FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@table) AND c.is_nullable = 0 AND c.default_object_id = 0
              AND c.is_identity = 0 AND c.is_computed = 0 AND t.name NOT IN ('timestamp', 'rowversion');
            """;
        command.Parameters.AddWithValue("@table", table);
        var columns = new List<(string, string, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetInt16(2)));
        return columns;
    }

    private static object Neutral(string type, int length) =>
        type switch
        {
            "uniqueidentifier" => Guid.NewGuid(),
            "nvarchar" or "nchar" => Text(length < 0 ? 32 : length / 2),
            "varchar" or "char" => Text(length < 0 ? 32 : length),
            "bit" => false,
            "tinyint" => (byte)0,
            "smallint" => (short)0,
            "int" => 0,
            "bigint" => 0L,
            "decimal" or "numeric" or "money" or "smallmoney" => 0m,
            "float" or "real" => 0d,
            "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.UtcNow,
            "datetimeoffset" => DateTimeOffset.UtcNow,
            "time" => TimeSpan.Zero,
            "varbinary" or "binary" => Guid.NewGuid().ToByteArray()[..(length < 0 ? 16 : Math.Min(length, 16))],
            _ => throw new NotSupportedException($"Column type '{type}' has no neutral value."),
        };

    // Texto y binarios distintos en cada fila, para no chocar con otros índices únicos.
    private static string Text(int length) => Guid.NewGuid().ToString("N")[..Math.Min(length, 32)];
}
