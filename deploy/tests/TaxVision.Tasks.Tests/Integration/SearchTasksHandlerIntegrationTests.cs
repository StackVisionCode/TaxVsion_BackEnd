using TaxVision.Tasks.Tests.ClientForms.Integration;
using TaxVision.Tasks.Tests.Tasks;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>`/tasks/search?due=` en SQL Server: el total de cada lista es el número del resumen, con dos alcances.</summary>
public sealed class SearchTasksHandlerIntegrationTests : IAsyncLifetime
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Due_filters_list_exactly_what_the_summary_counts(bool restricted)
    {
        await using var db = _database.NewContext(Guid.Empty);

        await SearchTasksHandlerTests.AssertMatchesSummaryAsync(db, _scenario, restricted ? _scenario.Boss : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Statutory_filter_lists_exactly_the_filing_deadlines_count(bool restricted)
    {
        await using var db = _database.NewContext(Guid.Empty);

        await SearchTasksHandlerTests.AssertStatutoryMatchesDeadlinesAsync(
            db,
            _scenario,
            restricted ? _scenario.Boss : null
        );
    }
}
