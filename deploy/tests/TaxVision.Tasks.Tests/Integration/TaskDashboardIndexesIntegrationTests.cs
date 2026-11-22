using Microsoft.Data.SqlClient;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Índices por SQL directo de TuneDashboardSummaryIndexes, en la base migrada: el top y los vencimientos legales leen
/// título y zona sin lookups, y la búsqueda ordena por creación dentro del tenant.
/// </summary>
public sealed class TaskDashboardIndexesIntegrationTests
{
    [Fact]
    public async Task The_due_index_covers_title_and_time_zone()
    {
        var columns = await ColumnsAsync("IX_Tasks_TenantId_Status_DueAtUtc");

        Assert.Equal(["TenantId", "Status", "DueAtUtc"], columns.Where(c => !c.Included).Select(c => c.Name));
        Assert.Contains(columns, c => c is { Name: "Title", Included: true });
        Assert.Contains(columns, c => c is { Name: "DueTimeZoneId", Included: true });
    }

    [Fact]
    public async Task The_search_orders_by_creation_within_the_tenant()
    {
        var columns = await ColumnsAsync("IX_Tasks_TenantId_CreatedAtUtc");

        Assert.Equal(
            [("TenantId", false), ("CreatedAtUtc", true), ("Id", false)],
            columns.Where(c => !c.Included).Select(c => (c.Name, c.Descending))
        );
    }

    private static async Task<List<(string Name, bool Included, bool Descending)>> ColumnsAsync(string index)
    {
        await using var connection = new SqlConnection(TasksIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, ic.is_included_column, ic.is_descending_key
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('Tasks') AND i.name = @index
            ORDER BY ic.is_included_column, ic.key_ordinal, c.name
            """;
        command.Parameters.AddWithValue("@index", index);
        var columns = new List<(string, bool, bool)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)));
        return columns;
    }
}
