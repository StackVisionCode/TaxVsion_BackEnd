using Microsoft.Data.SqlClient;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using TaxVision.Tasks.Tests.Tasks;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// El resumen del dashboard ejecutado en SQL Server: los mismos números que en InMemory con dos alcances, y los
/// índices de la migración AddTaskSummaryIndexes creados en la base.
/// </summary>
public sealed class TaskSummaryReaderIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly TaskSummaryScenario _scenario = new();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        await using var db = _database.NewContext(Guid.Empty);
        await _scenario.SeedAsync(db);
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Counts_follow_the_office_day_and_the_viewer_scope()
    {
        await using var db = _database.NewContext(Guid.Empty);

        await _scenario.AssertAsync(new TaskSummaryReader(db));
    }

    [Fact]
    public async Task The_summary_indexes_exist_after_migrating()
    {
        await using var connection = new SqlConnection(TasksIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('Tasks') "
            + "AND name IN ('IX_Tasks_TenantId_Status_DueAtUtc', 'IX_Tasks_TenantId_Status_CompletedAtUtc')";
        var names = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
        }

        Assert.Equal(["IX_Tasks_TenantId_Status_CompletedAtUtc", "IX_Tasks_TenantId_Status_DueAtUtc"], names.Order());
    }
}
