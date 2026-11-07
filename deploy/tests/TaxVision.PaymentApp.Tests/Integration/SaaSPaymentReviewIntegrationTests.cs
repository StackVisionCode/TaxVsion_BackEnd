using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>PAY-1 contra el SQL Server local: la marca de revisión se guarda, se relee y sale en la bandeja admin.</summary>
public sealed class SaaSPaymentReviewIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString =
        PaymentAppWolverineHost.Connection;

    private readonly Guid _tenantId = Guid.NewGuid();
    private Guid _paymentId;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo las filas de este tenant.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM dbo.WebhookEvents WHERE RelatedSaaSPaymentId = @payment; "
            + "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
            + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant;";
        command.Parameters.AddWithValue("@payment", _paymentId);
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migration_adds_review_columns_and_filtered_index()
    {
        var flaggedAt = new DateTime(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc);
        var payment = CancelledHostedCheckout();
        _paymentId = payment.Id;
        Assert.True(payment.FlagForReview("LateProviderSuccess", "pi_review_1", 4900, "USD", flaggedAt).IsSuccess);
        var webhook = WebhookEvent
            .Receive(
                PaymentProviderCode.Stripe,
                $"evt_{Guid.NewGuid():N}",
                "checkout.session.completed",
                "{}",
                "sig",
                flaggedAt
            )
            .Value;
        webhook.MarkProcessing(flaggedAt);
        webhook.MarkNeedsReview(payment.Id, "SaaSPayment.InvalidTransition", flaggedAt);

        await using (var write = CreateContext())
        {
            write.SaaSPayments.Add(payment);
            write.WebhookEvents.Add(webhook);
            await write.SaveChangesAsync();
        }

        await using (var read = CreateContext())
        {
            var stored = await read
                .SaaSPayments.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(p => p.Id == payment.Id);
            Assert.Equal(PaymentStatus.Cancelled, stored.Status);
            Assert.Equal(flaggedAt, stored.ReviewRequiredAtUtc);
            Assert.Equal("LateProviderSuccess", stored.ReviewReasonCode);
            Assert.Equal("pi_review_1", stored.ReviewProviderReference);
            Assert.Equal(4900, stored.ReviewPaidAmountCents);
            Assert.Equal("USD", stored.ReviewPaidCurrency);
            Assert.Null(stored.ReviewResolvedAtUtc);

            var storedWebhook = await read.WebhookEvents.AsNoTracking().SingleAsync(w => w.Id == webhook.Id);
            Assert.Equal(WebhookEventStatus.NeedsReview, storedWebhook.Status);

            var pending = await new SaaSPaymentReviewReader(read).ListAsync(resolved: false, CancellationToken.None);
            var row = Assert.Single(pending, r => r.PaymentId == payment.Id);
            Assert.Equal("LateProviderSuccess", row.ReviewReasonCode);
            Assert.Equal(webhook.ProviderEventId, row.WebhookEvent?.ProviderEventId);
            Assert.DoesNotContain(
                await new SaaSPaymentReviewReader(read).ListAsync(resolved: true, CancellationToken.None),
                r => r.PaymentId == payment.Id
            );
        }

        await using (var resolve = CreateContext())
        {
            var tracked = await resolve.SaaSPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == payment.Id);
            Assert.True(tracked.ResolveReview("NoActionNeeded", Guid.NewGuid(), flaggedAt.AddHours(1)).IsSuccess);
            await resolve.SaveChangesAsync();
        }

        await using (var after = CreateContext())
        {
            var reader = new SaaSPaymentReviewReader(after);
            Assert.DoesNotContain(
                await reader.ListAsync(resolved: false, CancellationToken.None),
                r => r.PaymentId == payment.Id
            );
            Assert.Contains(
                await reader.ListAsync(resolved: true, CancellationToken.None),
                r => r.PaymentId == payment.Id
            );
        }

        Assert.Equal(
            "([ReviewRequiredAtUtc] IS NOT NULL AND [ReviewResolvedAtUtc] IS NULL)",
            await ScalarAsync(
                "SELECT filter_definition FROM sys.indexes WHERE name = 'IX_SaaSPayments_ReviewPending' AND object_id = OBJECT_ID('dbo.SaaSPayments')"
            )
        );
    }

    private SaaSPayment CancelledHostedCheckout()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay1-review-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        var session = $"cs_pay1_{Guid.NewGuid():N}";
        payment.RecordHostedCheckoutSession(
            session,
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, session).Value,
            "https://checkout.stripe.test/session",
            nowUtc
        );
        payment.MarkCancelled("BuyerCancelled", nowUtc);
        return payment;
    }

    private PaymentAppDbContext CreateContext()
    {
        var tenant = new TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString).Options,
            tenant
        );
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync()) as string;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? Guid.Empty;
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
