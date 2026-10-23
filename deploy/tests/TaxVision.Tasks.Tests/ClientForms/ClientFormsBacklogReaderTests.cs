using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.ClientForms.Observability;
using TaxVision.Tasks.Infrastructure.Observability;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// El job lee sin tenant ambiente: los conteos cubren todas las oficinas y solo lo que pasó el umbral.
public sealed class ClientFormsBacklogReaderTests
{
    [Fact]
    public async Task Nothing_is_stale_before_the_threshold()
    {
        await using var db = await SeedAsync();

        var backlog = await new ClientFormsBacklogReader(db).ReadAsync(Now.AddMinutes(5));

        Assert.Equal(new ClientFormsBacklog(0, 1, 0, 0, 0), backlog);
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(48, 0, 1, 0)]
    [InlineData(96, 0, 0, 1)]
    public async Task Pending_work_is_counted_once_past_the_threshold(
        int hoursLater,
        long underOneDay,
        long oneToThreeDays,
        long overThreeDays
    )
    {
        await using var db = await SeedAsync();

        var backlog = await new ClientFormsBacklogReader(db).ReadAsync(Now.AddHours(hoursLater));

        Assert.Equal(new ClientFormsBacklog(1, underOneDay, oneToThreeDays, overThreeDays, 1), backlog);
    }

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var pending = CustomerRequest();
        Assert.True(
            pending
                .RegisterAttachment(
                    new FormAnswerAddress(FileComponent, null),
                    Guid.NewGuid(),
                    "return.pdf",
                    "application/pdf",
                    10,
                    2,
                    20,
                    null,
                    Now
                )
                .IsSuccess
        );
        var submitted = CustomerRequest();
        Submit(submitted);
        Assert.True(submitted.RequestAction("create_customer", "CreateCustomer", Guid.NewGuid(), Now).IsSuccess);
        db.FormRequests.AddRange(pending, submitted);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
