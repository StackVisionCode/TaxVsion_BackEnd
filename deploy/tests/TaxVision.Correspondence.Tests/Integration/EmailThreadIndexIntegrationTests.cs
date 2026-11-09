using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (COR-02): el mismo ProviderThreadId puede tener un hilo por cliente, pero no dos
/// del mismo cliente.
/// </summary>
public sealed class EmailThreadIndexIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private async Task InsertAsync(Guid customerId, string providerThreadId)
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        db.EmailThreads.Add(
            EmailThread.NewFromMessage(_tenantId, customerId, "Subject", providerThreadId, DateTime.UtcNow).Value
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SameProviderThreadId_TwoCustomers_BothInsert()
    {
        var customerA = Guid.NewGuid();
        await InsertAsync(customerA, "cor02-thread");
        await InsertAsync(Guid.NewGuid(), "cor02-thread");

        // El DbContext traduce la violación del índice único a ConflictException.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => InsertAsync(customerA, "cor02-thread"));
        Assert.IsType<DbUpdateException>(conflict.InnerException);
    }
}
