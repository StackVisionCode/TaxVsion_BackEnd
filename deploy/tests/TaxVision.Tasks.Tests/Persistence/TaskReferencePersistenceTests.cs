using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>El cliente y el año son un tipo propio: reemplazarlo tiene que llegar a la fila, en la raíz y en la hija.</summary>
public sealed class TaskReferencePersistenceTests
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
    public async Task A_new_client_and_year_are_saved_on_the_task_and_its_subtask()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var acme = Guid.NewGuid();
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
        await using (var seed = CreateContext())
        {
            seed.Tasks.AddRange(root, child);
            await seed.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var repository = new TaskRepository(db);
            var loaded = await repository.ListByIdsAsync(tenant, [root.Id, child.Id]);
            var reference = TaskReference.Create(acme, 2025).Value;
            Assert.True(
                loaded.Single(t => t.Id == root.Id).ChangeReference(reference, owner, DateTime.UtcNow).IsSuccess
            );
            loaded.Single(t => t.Id == child.Id).InheritReference(reference);
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext();
        var saved = await new TaskRepository(check).ListByIdsAsync(tenant, [root.Id, child.Id]);
        Assert.All(saved, t => Assert.Equal(((Guid?)acme, (int?)2025), (t.Reference.CustomerId, t.Reference.TaxYear)));
    }
}
