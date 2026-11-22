using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaxVision.CloudStorage.Tests.Integration.Performance;

/// <summary>
/// Siembra masiva, tiempos y planes de los resúmenes del dashboard. Siembra en SQL (INSERT … SELECT con un generador
/// de filas), mide con 3 vueltas de calentamiento y 30 medidas, y repite cada comando capturado con
/// STATISTICS IO y XML para sacar lecturas lógicas, búsquedas, recorridos y lookups.
/// </summary>
internal static partial class DashboardPerformance
{
    public const int WarmUp = 3;
    public const int Runs = 30;
    public const int TimeoutSeconds = 900;

    /// <summary>Tenants de ruido y su fracción del volumen del tenant medido (por defecto 3 con un tercio).</summary>
    public static readonly int NoiseTenants = Setting("DASHBOARD_PERF_NOISE_TENANTS", 3);
    public static readonly int NoiseDivisor = Setting("DASHBOARD_PERF_NOISE_DIVISOR", 3);

    public static string Scenario => $"ruido {NoiseTenants}×1/{NoiseDivisor}";

    /// <summary>El 1 es el medido; los demás, ruido.</summary>
    public static IReadOnlyList<Guid> AllTenants => [.. Enumerable.Range(1, NoiseTenants + 1).Select(Tenant)];

    public static int DivisorOf(int tenant) => tenant == 1 ? 1 : NoiseDivisor;

    /// <summary>Tenant de prueba: prefijo 7e57da7a, nunca un tenant real.</summary>
    public static Guid Tenant(int index) => Guid.Parse($"7e57da7a-0000-4000-8000-{index:D12}");

    private static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    /// <summary>Id determinista de la siembra, en SQL. <paramref name="kind"/> son 4 dígitos hex.</summary>
    public static string SqlId(string kind, int tenant, string n) =>
        $"CONVERT(uniqueidentifier, '7e57{kind}-{tenant:D4}-4000-8000-' + RIGHT('000000000000' + CONVERT(varchar(12), {n}), 12))";

    public static Guid Id(string kind, int tenant, long n) => Guid.Parse($"7e57{kind}-{tenant:D4}-4000-8000-{n:D12}");

    /// <summary>Copia una fila plantilla <paramref name="rows"/> veces; <c>n</c> va de 0 a rows-1.</summary>
    public static Task<int> CloneAsync(
        string connectionString,
        string table,
        string templateFilter,
        int rows,
        IReadOnlyDictionary<string, string> values
    ) => InsertAsync(connectionString, table, rows, values, templateFilter);

    /// <summary>Inserta filas generadas; las columnas obligatorias sin valor reciben uno neutro por tipo.</summary>
    public static async Task<int> InsertAsync(
        string connectionString,
        string table,
        int rows,
        IReadOnlyDictionary<string, string> values,
        string? templateFilter = null
    )
    {
        if (rows <= 0)
            return 0;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var columns = await ColumnsAsync(connection, table);
        var unknown = values.Keys.Where(k => !columns.Any(c => c.Name == k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"{table}: unknown columns {string.Join(", ", unknown)}");

        var names = new List<string>();
        var exprs = new List<string>();
        foreach (var column in columns.Where(c => c.Insertable))
        {
            string? expr =
                values.TryGetValue(column.Name, out var value) ? value
                : templateFilter is not null ? $"src.[{column.Name}]"
                : column.Nullable || column.HasDefault ? null
                : Neutral(column);
            if (expr is null)
                continue;
            names.Add($"[{column.Name}]");
            exprs.Add(expr);
        }

        var from = templateFilter is null
            ? ""
            : $" CROSS JOIN (SELECT TOP (1) * FROM {table} WHERE {templateFilter}) AS src";
        var sql = $"""
            INSERT INTO {table} ({string.Join(", ", names)})
            SELECT {string.Join(", ", exprs)}
            FROM (SELECT TOP (@rows) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS int) AS n
                  FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS g{from};
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSeconds };
        command.Parameters.AddWithValue("@rows", rows);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Borra solo las filas de los tenants de prueba, tabla por tabla (hijas primero).</summary>
    public static async Task DeleteAsync(string connectionString, IReadOnlyList<Guid> tenants, params string[] tables)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            await using var command = new SqlCommand(
                $"DELETE FROM {table} WHERE {TenantPredicate(table, tenants)}",
                connection
            )
            {
                CommandTimeout = TimeoutSeconds,
            };
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Borra filas hijas sin TenantId por su madre, acotadas a las madres de los tenants de prueba.</summary>
    public static async Task DeleteChildrenAsync(
        string connectionString,
        string childTable,
        string foreignKey,
        string parentTable,
        IReadOnlyList<Guid> tenants
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"DELETE FROM {childTable} WHERE [{foreignKey}] IN (SELECT [Id] FROM {parentTable} WHERE {TenantPredicate(parentTable, tenants)})",
            connection
        )
        {
            CommandTimeout = TimeoutSeconds,
        };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>SELECT COUNT(*) por tabla de las filas de los tenants de prueba.</summary>
    public static async Task<IReadOnlyDictionary<string, int>> CountAsync(
        string connectionString,
        IReadOnlyList<Guid> tenants,
        params string[] tables
    )
    {
        var counts = new Dictionary<string, int>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            await using var command = new SqlCommand(
                $"IF OBJECT_ID(N'{table}') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM {table} WHERE {TenantPredicate(table, tenants)}",
                connection
            )
            {
                CommandTimeout = TimeoutSeconds,
            };
            counts[table] = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        return counts;
    }

    public static async Task UpdateStatisticsAsync(string connectionString, params string[] tables)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            await using var command = new SqlCommand($"UPDATE STATISTICS {table} WITH FULLSCAN", connection)
            {
                CommandTimeout = TimeoutSeconds,
            };
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>3 de calentamiento, 30 medidas y una vuelta capturada para repetir sus comandos con estadísticas.</summary>
    public static async Task<PerfResult> MeasureAsync(
        string service,
        string piece,
        string scope,
        string connectionString,
        Func<CommandCapture, Task> run
    )
    {
        var capture = new CommandCapture();
        for (var i = 0; i < WarmUp; i++)
            await run(capture);

        var samples = new double[Runs];
        for (var i = 0; i < Runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await run(capture);
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        capture.Enabled = true;
        await run(capture);
        capture.Enabled = false;

        var plans = new List<CommandPlan>();
        foreach (var command in capture.Commands)
            plans.Add(await ExplainAsync(connectionString, command));

        Array.Sort(samples);
        var result = new PerfResult(service, piece, scope, samples[Runs / 2 - 1], samples[28], samples[^1], plans);
        Report(result);
        return result;
    }

    // Repite el comando con STATISTICS IO (lecturas por tabla) y XML (plan real).
    private static async Task<CommandPlan> ExplainAsync(string connectionString, CapturedCommand captured)
    {
        await using var connection = new SqlConnection(connectionString);
        var messages = new StringBuilder();
        connection.InfoMessage += (_, e) => messages.AppendLine(e.Message);
        await connection.OpenAsync();
        await using (var on = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS XML ON;", connection))
            await on.ExecuteNonQueryAsync();

        var plans = new List<string>();
        await using (var command = new SqlCommand(captured.Text, connection) { CommandTimeout = TimeoutSeconds })
        {
            foreach (var parameter in captured.Parameters)
                command.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.Ordinal);
                while (await reader.ReadAsync())
                    if (isPlan)
                        plans.Add(reader.GetString(0));
            } while (await reader.NextResultAsync());
        }

        var reads = TableReads()
            .Matches(messages.ToString())
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.Sum(m => long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        return new CommandPlan(captured.Text, reads, plans.SelectMany(Operators).ToList());
    }

    private static IEnumerable<PlanOperator> Operators(string xml)
    {
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        foreach (var op in XDocument.Parse(xml).Descendants(ns + "RelOp"))
        {
            var physical = (string?)op.Attribute("PhysicalOp") ?? "";
            var access = op.Elements().FirstOrDefault(e => e.Name == ns + "IndexScan" || e.Name == ns + "TableScan");
            if (access is null && physical != "RID Lookup")
                continue;
            var target = access?.Element(ns + "Object");
            var counters =
                op.Element(ns + "RunTimeInformation")?.Elements(ns + "RunTimeCountersPerThread").ToList() ?? [];
            long Sum(string name) => counters.Sum(c => long.TryParse((string?)c.Attribute(name), out var v) ? v : 0);
            var lookup = (string?)access?.Attribute("Lookup") is "1" or "true" || physical == "RID Lookup";
            yield return new PlanOperator(
                lookup ? "Key Lookup" : physical,
                Clean((string?)target?.Attribute("Table")),
                Clean((string?)target?.Attribute("Index")),
                Sum("ActualRows"),
                counters.Any(c => c.Attribute("ActualRowsRead") is not null)
                    ? Sum("ActualRowsRead")
                    : Sum("ActualRows"),
                Sum("ActualExecutions")
            );
        }
    }

    private static string Clean(string? name) => (name ?? "").Trim('[', ']');

    private static void Report(PerfResult result)
    {
        var directory =
            Environment.GetEnvironmentVariable("DASHBOARD_PERF_OUT")
            ?? Path.Combine(Path.GetTempPath(), "taxvision-dashboard-perf");
        Directory.CreateDirectory(directory);
        var row = string.Create(
            CultureInfo.InvariantCulture,
            $"| {result.Service} | {result.Piece} | {result.Scope} | {result.P50:0.0} | {result.P95:0.0} | {result.Max:0.0} | {result.LogicalReads} | {result.Access} | {Scenario} |"
        );
        File.AppendAllText(Path.Combine(directory, "results.md"), row + Environment.NewLine);

        var detail = new StringBuilder()
            .AppendLine($"## {result.Service} · {result.Piece} · {result.Scope}")
            .AppendLine(row);
        foreach (var plan in result.Plans)
        {
            detail.AppendLine("```sql").AppendLine(plan.Sql).AppendLine("```");
            detail.AppendLine("Lecturas: " + string.Join(", ", plan.Reads.Select(r => $"{r.Key}={r.Value}")));
            foreach (var op in plan.Operators)
                detail.AppendLine(
                    $"- {op.Physical} {op.Table}.{op.Index} filas={op.ActualRows} leídas={op.RowsRead} ejecuciones={op.Executions}"
                );
        }
        File.AppendAllText(Path.Combine(directory, "details.md"), detail.AppendLine().ToString());
    }

    private static string TenantPredicate(string table, IReadOnlyList<Guid> tenants) =>
        $"[TenantId] IN ({string.Join(", ", tenants.Select(t => $"'{t}'"))})";

    private static string Neutral(Column column) =>
        column.Type switch
        {
            "uniqueidentifier" => "NEWID()",
            "nvarchar" or "varchar" or "nchar" or "char" => "N'x'",
            "int"
            or "bigint"
            or "smallint"
            or "tinyint"
            or "bit"
            or "decimal"
            or "numeric"
            or "float"
            or "real"
            or "money" => "0",
            "datetime2" or "datetime" or "smalldatetime" or "date" => "'2026-01-01'",
            "datetimeoffset" => "'2026-01-01T00:00:00+00:00'",
            "time" => "'00:00'",
            "varbinary" or "binary" => "0x00",
            _ => throw new InvalidOperationException($"No neutral value for {column.Name} ({column.Type})"),
        };

    private static async Task<List<Column>> ColumnsAsync(SqlConnection connection, string table)
    {
        await using var command = new SqlCommand(
            """
            SELECT c.name, t.name, c.is_nullable, c.is_identity, c.is_computed, c.default_object_id
            FROM sys.columns AS c JOIN sys.types AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@table) ORDER BY c.column_id
            """,
            connection
        );
        command.Parameters.AddWithValue("@table", table);
        var columns = new List<Column>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(
                new Column(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    !reader.GetBoolean(3) && !reader.GetBoolean(4) && reader.GetString(1) != "timestamp",
                    reader.GetInt32(5) != 0
                )
            );
        if (columns.Count == 0)
            throw new InvalidOperationException($"Table {table} not found");
        return columns;
    }

    [GeneratedRegex(@"Table '([^']+)'\. Scan count \d+, logical reads (\d+)")]
    private static partial Regex TableReads();

    private sealed record Column(string Name, string Type, bool Nullable, bool Insertable, bool HasDefault);
}

/// <summary>Captura los comandos de EF de una vuelta para repetirlos con estadísticas.</summary>
internal sealed class CommandCapture : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public List<CapturedCommand> Commands { get; } = [];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbCommand command)
    {
        if (!Enabled)
            return;
        Commands.Add(
            new CapturedCommand(
                command.CommandText,
                [.. command.Parameters.Cast<SqlParameter>().Select(p => (SqlParameter)((ICloneable)p).Clone())]
            )
        );
    }
}

internal sealed record CapturedCommand(string Text, SqlParameter[] Parameters);

internal sealed record PlanOperator(
    string Physical,
    string Table,
    string Index,
    long ActualRows,
    long RowsRead,
    long Executions
);

internal sealed record CommandPlan(
    string Sql,
    IReadOnlyDictionary<string, long> Reads,
    IReadOnlyList<PlanOperator> Operators
)
{
    public long LogicalReads => Reads.Values.Sum();
}

internal sealed record PerfResult(
    string Service,
    string Piece,
    string Scope,
    double P50,
    double P95,
    double Max,
    IReadOnlyList<CommandPlan> Plans
)
{
    public long LogicalReads => Plans.Sum(p => p.LogicalReads);

    /// <summary>Recorridos y lookups de todos los comandos; si no hay, las búsquedas.</summary>
    public string Access
    {
        get
        {
            var ops = Plans.SelectMany(p => p.Operators).ToList();
            var bad = ops.Where(o =>
                    o.Physical is "Key Lookup" or "Table Scan" or "Clustered Index Scan" or "Index Scan"
                )
                .GroupBy(o => (o.Physical, o.Table, o.Index))
                .Select(g =>
                    $"{g.Key.Physical} {g.Key.Table}.{g.Key.Index} ({g.Sum(o => o.RowsRead)} filas, {g.Sum(o => o.Executions)} ejec.)"
                )
                .ToList();
            var seeks = ops.Where(o => o.Physical is "Index Seek" or "Clustered Index Seek")
                .Select(o => $"{o.Table}.{o.Index}")
                .Distinct();
            return (bad.Count == 0 ? "solo seeks: " : string.Join("; ", bad) + " · seeks: ") + string.Join(", ", seeks);
        }
    }
}
