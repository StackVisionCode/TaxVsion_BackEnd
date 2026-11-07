using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-07: dos ingestas del mismo hilo desde lecturas viejas ya no pisan el contador.</summary>
public sealed class EmailThreadConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoAppendsFromStaleReads_SecondThrowsConcurrency()
    {
        var at = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var thread = EmailThread.NewFromMessage(_tenantId, Guid.NewGuid(), "Subject", "pt-1", at).Value;
        for (var i = 0; i < 4; i++)
            thread.AppendMessage(at.AddMinutes(i + 1));
        await using (var seed = CorrespondenceSqlServer.NewContext(_tenantId))
        {
            seed.EmailThreads.Add(thread);
            await seed.SaveChangesAsync();
        }

        await using var first = CorrespondenceSqlServer.NewContext(_tenantId);
        await using var second = CorrespondenceSqlServer.NewContext(_tenantId);
        var a = await first.EmailThreads.FirstAsync(x => x.Id == thread.Id);
        var b = await second.EmailThreads.FirstAsync(x => x.Id == thread.Id);
        Assert.Equal(5, a.MessageCount);

        a.AppendMessage(at.AddHours(1));
        await first.SaveChangesAsync();

        b.AppendMessage(at.AddHours(2));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        // El reintento relee y cuenta los dos.
        await using var retry = CorrespondenceSqlServer.NewContext(_tenantId);
        var reread = await retry.EmailThreads.FirstAsync(x => x.Id == thread.Id);
        reread.AppendMessage(at.AddHours(2));
        await retry.SaveChangesAsync();

        await using var check = CorrespondenceSqlServer.NewContext(_tenantId);
        Assert.Equal(7, (await check.EmailThreads.AsNoTracking().FirstAsync(x => x.Id == thread.Id)).MessageCount);
    }
}
