using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-02: la reparación corre por Wolverine con SQL real, en seco por defecto y una unidad por hilo.</summary>
public sealed class RepairCrossCustomerThreadsIntegrationTests(CorrespondenceMediatorFactory factory)
    : IClassFixture<CorrespondenceMediatorFactory>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    // Hilo de A con 2 correos de A y 1 de B.
    private async Task<Guid> SeedMixedThreadAsync(string providerThreadId)
    {
        var t0 = DateTime.UtcNow.AddDays(-3);
        var thread = EmailThread.NewFromMessage(_tenantId, _customerA, "Subject", providerThreadId, t0).Value;
        thread.AppendMessage(t0.AddDays(1));
        thread.AppendMessage(t0.AddDays(2));
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        db.EmailThreads.Add(thread);
        db.IncomingEmails.Add(NewEmail(_customerA, thread.Id, t0));
        db.IncomingEmails.Add(NewEmail(_customerA, thread.Id, t0.AddDays(1)));
        db.IncomingEmails.Add(NewEmail(_customerB, thread.Id, t0.AddDays(2)));
        await db.SaveChangesAsync();
        return thread.Id;
    }

    private IncomingEmail NewEmail(Guid customerId, Guid threadId, DateTime receivedAtUtc) =>
        IncomingEmail
            .Create(
                _tenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                Guid.NewGuid().ToString(),
                EmailAddress.Create("sender@cor02.test").Value,
                null,
                "Subject",
                "",
                receivedAtUtc,
                false,
                0
            )
            .Value;

    private async Task<RepairCrossCustomerThreadsReport> RunAsync(RepairCrossCustomerThreadsCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<RepairCrossCustomerThreadsReport>(command);
    }

    private async Task<int> ForeignEmailsAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        return await db
            .IncomingEmails.IgnoreQueryFilters()
            .CountAsync(e =>
                e.TenantId == _tenantId
                && db.EmailThreads.IgnoreQueryFilters()
                    .Any(t => t.Id == e.EmailThreadId && t.CustomerId != null && t.CustomerId != e.CustomerId)
            );
    }

    [Fact]
    public async Task DryRunReports_ThenApplyRepairsEachThread_ThenNothingIsLeft()
    {
        var first = await SeedMixedThreadAsync($"cor02-{Guid.NewGuid():N}");
        var second = await SeedMixedThreadAsync($"cor02-{Guid.NewGuid():N}");

        var dryRun = await RunAsync(new RepairCrossCustomerThreadsCommand(_tenantId));
        Assert.True(dryRun.DryRun);
        Assert.Equal((2, 2, 0), (dryRun.ThreadsFound, dryRun.EmailsFound, dryRun.EmailsMoved));
        Assert.Equal(2, await ForeignEmailsAsync());

        var applied = await RunAsync(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false));
        Assert.Equal(
            (2, 2, 2, 2),
            (applied.ThreadsRepaired, applied.EmailsMoved, applied.ThreadsCreated, applied.ThreadsFound)
        );
        Assert.Empty(applied.FailedThreadIds);
        Assert.Equal(0, await ForeignEmailsAsync());

        await using (var db = CorrespondenceSqlServer.NewContext(_tenantId))
        {
            var threads = await db
                .EmailThreads.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TenantId == _tenantId)
                .ToListAsync();
            Assert.Equal(4, threads.Count);
            Assert.All(threads.Where(t => t.Id == first || t.Id == second), t => Assert.Equal(2, t.MessageCount));
            Assert.All(threads.Where(t => t.CustomerId == _customerB), t => Assert.Equal(1, t.MessageCount));
        }

        var again = await RunAsync(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false));
        Assert.Equal((0, 0), (again.ThreadsFound, again.EmailsMoved));
    }
}
