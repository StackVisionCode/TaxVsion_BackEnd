using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>Un pago en revisión no se reintenta: un éxito tardío ya marcado se cobraría dos veces.</summary>
public sealed class DunningReviewIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString = PaymentAppWolverineHost.Connection;

    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo las filas de este tenant.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
            + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant;";
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task PaymentUnderReview_IsNotDueForRetry()
    {
        var nowUtc = DateTime.UtcNow;
        var flagged = FailedPayment(nowUtc);
        flagged.FlagForReview(SaaSPayment.ReviewReasonLateProviderSuccess, "pi_late", 4900, "USD", nowUtc);
        var regular = FailedPayment(nowUtc);
        await using var context = CreateContext();
        context.SaaSPayments.AddRange(flagged, regular);
        await context.SaveChangesAsync();
        var repository = new SaaSPaymentRepository(context);

        var due = await repository.GetDueForRetryAsync(nowUtc, 5000);

        Assert.Contains(due, payment => payment.Id == regular.Id);
        Assert.DoesNotContain(due, payment => payment.Id == flagged.Id);
    }

    [Fact]
    public async Task PaymentUnderReview_IsNotCountedAsDue()
    {
        var nowUtc = DateTime.UtcNow;
        await using var context = CreateContext();
        var repository = new SaaSPaymentRepository(context);
        var before = await repository.CountDueForRetryAsync(nowUtc);
        var flagged = FailedPayment(nowUtc);
        flagged.FlagForReview(SaaSPayment.ReviewReasonLateProviderSuccess, "pi_late", 4900, "USD", nowUtc);
        context.SaaSPayments.Add(flagged);
        await context.SaveChangesAsync();

        var after = await repository.CountDueForRetryAsync(nowUtc);

        Assert.Equal(before, after);
    }

    private SaaSPayment FailedPayment(DateTime nowUtc)
    {
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"dunning-review-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkFailed(
            "card_declined",
            "Declined",
            DunningDecision.RetryAt(nowUtc.AddMinutes(-5)),
            Guid.Empty,
            nowUtc
        );
        return payment;
    }

    private PaymentAppDbContext CreateContext()
    {
        var tenant = new ReconcileStuckSaaSPaymentIntegrationTests.TestTenantContext();
        tenant.SetTenant(_tenantId);
        var options = new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString);
        return new PaymentAppDbContext(options.Options, tenant);
    }
}
