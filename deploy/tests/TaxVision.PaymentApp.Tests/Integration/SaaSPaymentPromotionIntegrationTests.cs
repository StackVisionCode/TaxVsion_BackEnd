using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>Contra el SQL Server local: las promociones del pago del alta se guardan y vuelven con el pago.</summary>
public sealed class SaaSPaymentPromotionIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString = PaymentAppWolverineHost.Connection;
    private Guid _paymentId;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo el pago de esta prueba (las promociones se borran en cascada).
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.SaaSPayments WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", _paymentId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_promotions_are_saved_and_read_back_with_the_payment()
    {
        var promo = Guid.NewGuid();
        var gift = Guid.NewGuid();
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create($"onboarding-promotions-{Guid.NewGuid():N}").Value,
                Money.Create(9_900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;
        Assert.True(
            payment
                .AttachPromotions(
                    [
                        SaaSPaymentPromotion.Create(promo, 2_000, "snap-promo", 1).Value,
                        SaaSPaymentPromotion.Create(gift, 1_000, "snap-gift", 2).Value,
                    ],
                    12_900
                )
                .IsSuccess
        );
        _paymentId = payment.Id;

        await using (var db = CreateContext())
        {
            db.SaaSPayments.Add(payment);
            await db.SaveChangesAsync();
        }

        await using var reader = CreateContext();
        var stored = await reader.SaaSPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == _paymentId);

        Assert.Equal(
            [(promo, 2_000L, 1), (gift, 1_000L, 2)],
            stored.Promotions.OrderBy(p => p.Order).Select(p => (p.CodeReservationId, p.DiscountAmountCents, p.Order))
        );
    }

    private PaymentAppDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString).Options,
            new ReconcileStuckSaaSPaymentIntegrationTests.TestTenantContext()
        );
}
