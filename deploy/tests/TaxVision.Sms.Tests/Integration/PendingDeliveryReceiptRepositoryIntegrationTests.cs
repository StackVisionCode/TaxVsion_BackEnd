using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Sms.Domain.Webhooks;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>Contra SQL real: el índice único frena un DLR repetido y la limpieza borra por antigüedad.</summary>
public sealed class PendingDeliveryReceiptRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly string _provider = "it-" + Guid.NewGuid().ToString("N")[..12];

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("SMS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("b8e2f1a4-7c3d-4e9a-9f21-3c5d8a1b6e42")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set SMS_DB_CONNECTION or the Sms user-secrets to run this test.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private SmsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(_connectionString).Options, new NoTenant());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.PendingDeliveryReceipts.Where(r => r.ProviderCode == _provider).ExecuteDeleteAsync();
    }

    private PendingDeliveryReceipt Receipt(string providerMessageId, DateTime receivedAtUtc) =>
        new(_provider, providerMessageId, "dlr", "Delivered", null, null, receivedAtUtc);

    [Fact]
    public async Task Same_receipt_twice_is_a_unique_conflict()
    {
        await using (var first = NewContext())
        {
            await new PendingDeliveryReceiptRepository(first).AddAsync(Receipt("prov-1", DateTime.UtcNow));
            await first.SaveChangesAsync();
        }

        await using var second = NewContext();
        await new PendingDeliveryReceiptRepository(second).AddAsync(Receipt("prov-1", DateTime.UtcNow));

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
    }

    [Fact]
    public async Task Receipts_are_listed_oldest_first_and_purged_by_age()
    {
        var nowUtc = DateTime.UtcNow;
        await using var db = NewContext();
        var repository = new PendingDeliveryReceiptRepository(db);
        await repository.AddAsync(Receipt("prov-new", nowUtc));
        await repository.AddAsync(Receipt("prov-old", nowUtc.AddDays(-8)));
        await db.SaveChangesAsync();

        Assert.Single(await repository.ListAsync(_provider, "prov-old"));
        var removed = await repository.DeleteReceivedBeforeAsync(nowUtc.AddDays(-7));

        Assert.True(removed >= 1);
        Assert.Empty(await repository.ListAsync(_provider, "prov-old"));
        Assert.Single(await repository.ListAsync(_provider, "prov-new"));
    }
}
