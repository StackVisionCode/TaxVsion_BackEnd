using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// La lista muestra las tareas principales y, además, las subtareas que me tocan, con el nombre de la madre (D-8).
/// </summary>
public sealed class TaskListSubtasksPersistenceTests
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
    public async Task My_subtasks_join_the_list_with_their_parent_title()
    {
        var tenant = Guid.NewGuid();
        var (me, beto) = (Guid.NewGuid(), Guid.NewGuid());
        var root = NewTask(tenant, beto, "1040 — Ana López");
        var mine = Sub(root, me, "Collect W-2");
        var notMine = Sub(root, beto, "Review deductions");
        await using (var seed = CreateContext())
        {
            seed.Tasks.AddRange(root, mine, notMine);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var page = await SearchTasksHandler.Handle(
            new SearchTasksQuery(
                tenant,
                new TaskQueryFilter(RootOnly: true, AlsoSubtasksOf: me),
                1,
                20,
                me,
                CanViewAll: true,
                NowUtc: DateTime.UtcNow
            ),
            new TaskRepository(db),
            new WorkflowStatusRepository(db),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            CancellationToken.None
        );

        Assert.Equal(new[] { root.Id, mine.Id }.Order(), page.Items.Select(t => t.Id).Order());
        Assert.Equal("1040 — Ana López", page.Items.Single(t => t.Id == mine.Id).ParentTitle);
        Assert.Null(page.Items.Single(t => t.Id == root.Id).ParentTitle);
    }

    private static TaskItem NewTask(Guid tenant, Guid owner, string title) =>
        TaskItem
            .Create(
                tenant,
                owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;

    private static TaskItem Sub(TaskItem parent, Guid assignee, string title) =>
        TaskItem
            .CreateSubtask(
                parent,
                parent.CreatedByUserId,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;
}
