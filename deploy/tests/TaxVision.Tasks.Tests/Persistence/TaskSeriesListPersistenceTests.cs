using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>El listado de series en SQL: "mías" por dueño efectivo o responsable, y los clientes que cada uno ve.</summary>
public sealed class TaskSeriesListPersistenceTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static TasksDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    [Fact]
    public async Task Mine_and_client_visibility_are_resolved_by_the_database()
    {
        var tenant = Guid.NewGuid();
        var (me, beto) = (Guid.NewGuid(), Guid.NewGuid());
        var (myClient, otherClient) = (Guid.NewGuid(), Guid.NewGuid());
        var handedToMe = NewSeries(tenant, beto, beto, null);
        handedToMe.TransferOwnership(me);
        var ofMyClient = NewSeries(tenant, beto, beto, myClient);
        var ofOtherClient = NewSeries(tenant, beto, beto, otherClient);
        var common = NewSeries(tenant, beto, beto, null);
        await using (var seed = CreateContext())
        {
            seed.TaskSeries.AddRange(handedToMe, ofMyClient, ofOtherClient, common);
            seed.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(tenant, myClient, me, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var repository = new TaskSeriesRepository(db);
        var mine = await repository.ListPageAsync(new SeriesListFilter(tenant, null, me, null, 1, 20));
        var visible = await repository.ListPageAsync(new SeriesListFilter(tenant, null, null, me, 1, 20));

        Assert.Equal([handedToMe.Id], mine.Items.Select(s => s.Id));
        Assert.Equal(
            new[] { handedToMe.Id, ofMyClient.Id, common.Id }.Order(),
            visible.Items.Select(s => s.Id).Order()
        );
        Assert.Equal(3, visible.Total);
    }

    private static TaskSeries NewSeries(Guid tenant, Guid createdBy, Guid assignee, Guid? customer) =>
        TaskSeries
            .Create(
                tenant,
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
