using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>La versión que vio el usuario se exige al guardar: si otro guardó en medio, el segundo no pisa al primero.</summary>
public sealed class TaskVersionPersistenceTests
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
    public async Task Saving_with_the_version_read_before_another_save_is_a_conflict()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var task = TaskItem
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
        await using (var seed = CreateContext())
        {
            seed.Tasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var mine = CreateContext();
        var myRepository = new TaskRepository(mine);
        var myCopy = (await myRepository.GetByIdAsync(tenant, task.Id)).Value;
        var seen = myCopy.RowVersion.ToArray();

        await using (var theirs = CreateContext())
        {
            var theirCopy = (await new TaskRepository(theirs).GetByIdAsync(tenant, task.Id)).Value;
            theirCopy.ChangePriority(TaskPriority.High, owner, DateTime.UtcNow);
            await theirs.SaveChangesAsync();
        }

        myCopy.ChangeTitle(TaskTitle.Create("Prepare 1040-X").Value);
        myRepository.ExpectVersion(myCopy, seen);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => mine.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }
}
