using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// La papelera contra SQL Server: el filtro la oculta al resto del servicio, la lista la muestra por raíz y la purga
/// borra la rama vencida (las claves foráneas obligan a borrar las hijas antes que la madre).
/// </summary>
public sealed class TaskTrashPersistenceTests
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
    public async Task Trashed_branch_is_hidden_listed_by_its_root_and_purged_when_expired()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var deletedAt = DateTime.UtcNow.AddDays(-(TaskItem.TrashRetentionDays + 1));
        var (root, child) = await SeedTrashedBranchAsync(tenant, owner, deletedAt);

        await using (var db = CreateContext())
        {
            var repository = new TaskRepository(db);
            Assert.True((await repository.GetByIdAsync(tenant, root.Id)).IsFailure);
            Assert.True((await repository.GetIncludingTrashAsync(tenant, child.Id)).IsSuccess);

            var reader = new TaskTrashReader(db);
            var page = await reader.ListAsync(tenant, null, 1, 20);
            var item = Assert.Single(page.Items);
            Assert.Equal((root.Id, 1), (item.TaskId, item.SubtaskCount));
            Assert.Empty((await reader.ListAsync(tenant, Guid.NewGuid(), 1, 20)).Items);

            var expired = await reader.ListExpiredRootsAsync(
                DateTime.UtcNow.AddDays(-TaskItem.TrashRetentionDays),
                1000
            );
            Assert.Contains((tenant, root.Id), expired);
        }

        await using (var db = CreateContext())
        {
            var repository = new TaskRepository(db);
            foreach (var task in await repository.ListTrashBranchAsync(tenant, root.Id))
                repository.Remove(task);
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext();
        Assert.False(await check.Tasks.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant));
    }

    [Fact]
    public async Task A_dependency_on_a_task_that_does_not_exist_is_rejected_by_the_database()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var (root, _) = await SeedTrashedBranchAsync(tenant, owner, DateTime.UtcNow);

        await using var db = CreateContext();
        db.TaskDependencies.Add(TaskDependency.Create(tenant, root.Id, Guid.NewGuid(), owner, DateTime.UtcNow).Value);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<(TaskItem Root, TaskItem Child)> SeedTrashedBranchAsync(
        Guid tenant,
        Guid owner,
        DateTime deletedAt
    )
    {
        var root = TaskItem
            .Create(
                tenant,
                owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;
        var child = TaskItem
            .CreateSubtask(
                root,
                owner,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;
        root.Delete(owner, deletedAt, root.Id);
        child.Delete(owner, deletedAt, root.Id);

        await using var db = CreateContext();
        db.Tasks.AddRange(root, child);
        await db.SaveChangesAsync();
        return (root, child);
    }
}
