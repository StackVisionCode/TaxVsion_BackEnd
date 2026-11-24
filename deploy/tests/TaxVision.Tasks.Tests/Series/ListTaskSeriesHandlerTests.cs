using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Series.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Series;

/// <summary>La lista de series pagina, filtra "mías" y respeta los clientes que cada uno ve (S-06).</summary>
public sealed class ListTaskSeriesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();

    [Fact]
    public async Task Mine_lists_what_I_created_or_I_am_assigned()
    {
        var repo = new InMemoryTaskSeriesRepository(
            NewSeries(Me, Beto, null),
            NewSeries(Beto, Me, null),
            NewSeries(Beto, Beto, null)
        );

        var page = await ListAsync(repo, mine: true);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Without_seeing_every_client_a_series_of_someone_elses_client_is_hidden()
    {
        var hidden = NewSeries(Beto, Beto, Guid.NewGuid());
        var common = NewSeries(Beto, Beto, null);
        var repo = new InMemoryTaskSeriesRepository(hidden, common);

        var page = await ListAsync(repo, mine: false);

        Assert.Equal([common.Id], page.Items.Select(s => s.Id));
    }

    [Fact]
    public async Task It_pages()
    {
        var repo = new InMemoryTaskSeriesRepository([.. Enumerable.Range(0, 5).Select(_ => NewSeries(Me, Me, null))]);

        var page = await ListAsync(repo, mine: false, size: 2, number: 3);

        Assert.Single(page.Items);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task A_series_of_a_client_I_do_not_see_is_not_found_by_id()
    {
        var hidden = NewSeries(Beto, Beto, Guid.NewGuid());
        var repo = new InMemoryTaskSeriesRepository(hidden);

        var read = await GetTaskSeriesByIdHandler.Handle(
            new GetTaskSeriesByIdQuery(TenantId, hidden.Id, Me, CanViewAll: false),
            repo,
            new OnlyTheseCustomers(),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

        Assert.Equal(TaskErrors.Series.NotFound, read.Error);
    }

    private static Task<TaskSeriesPage> ListAsync(
        InMemoryTaskSeriesRepository repo,
        bool mine,
        int size = 20,
        int number = 1
    ) =>
        ListTaskSeriesHandler.Handle(
            new ListTaskSeriesQuery(TenantId, null, Me, CanViewAll: false, Mine: mine, Page: number, Size: size),
            repo,
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

    private static TaskSeries NewSeries(Guid createdBy, Guid assignee, Guid? customer) =>
        TaskSeries
            .Create(
                TenantId,
                createdBy,
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly bookkeeping").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.Create(customer, null).Value,
                    AssigneeUserId = assignee,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(10),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
}
