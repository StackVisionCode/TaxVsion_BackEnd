using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Infrastructure.Persistence;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>El webhook y la conciliación escriben el mismo mensaje: contra SQL real, el segundo guardado choca.</summary>
public sealed class SmsMessageConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();

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
        await db.SmsMessages.IgnoreQueryFilters().Where(m => m.TenantId == _tenant).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedAcceptedAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var message = SmsMessage
            .Create(
                _tenant,
                Guid.NewGuid(),
                PhoneE164.Create("+15550201234").Value,
                SmsBody.Create("integration test").Value,
                "it-" + Guid.NewGuid().ToString("N"),
                "it-corr",
                Guid.NewGuid(),
                "fake",
                "integration",
                [],
                nowUtc
            )
            .Value;
        message.MarkAccepted("prov-" + Guid.NewGuid().ToString("N"), nowUtc);

        await using var db = NewContext();
        db.SmsMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    [Fact]
    public async Task Webhook_and_reconcile_on_same_message_second_save_conflicts()
    {
        var id = await SeedAcceptedAsync();
        await using var webhook = NewContext();
        await using var reconcile = NewContext();
        var fromWebhook = await webhook.SmsMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == id);
        var fromReconcile = await reconcile.SmsMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == id);

        fromWebhook.MarkDelivered(DateTime.UtcNow);
        await webhook.SaveChangesAsync();

        fromReconcile.MarkFailed(DateTime.UtcNow, "late", "late");
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => reconcile.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = NewContext();
        var stored = await read.SmsMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == id);
        Assert.Equal(SmsMessageStatus.Delivered, stored.Status);
    }
}
