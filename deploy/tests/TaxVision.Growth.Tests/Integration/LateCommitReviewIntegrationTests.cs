using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra el SQL Server real: dos contextos registran la misma revisión (reserva + evento) a la vez.
/// Queda una sola fila y el segundo no rompe. Tenant propio; se borra al terminar.
/// </summary>
public sealed class LateCommitReviewIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();
    private readonly Guid _sourceEventId = Guid.NewGuid();

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("GROWTH_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("799f9d80-8d1c-47d2-925d-taxvision-growth")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set GROWTH_DB_CONNECTION or the Growth user-secrets to run this test.");

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private GrowthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<GrowthDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.LateCommitReviews.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ConcurrentLateCommits_SameEvent_SingleRow()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var firstRepository = new LateCommitReviewRepository(first, new FixedTenant(_tenantId));
        var secondRepository = new LateCommitReviewRepository(second, new FixedTenant(_tenantId));

        var results = await Task.WhenAll(
            firstRepository.RecordAsync(NewReview()),
            secondRepository.RecordAsync(NewReview())
        );

        Assert.Single(results, recorded => recorded);
        await using var verify = NewContext();
        Assert.Equal(
            1,
            await verify.LateCommitReviews.IgnoreQueryFilters().CountAsync(r => r.TenantId == _tenantId)
        );

        // Una entrega posterior del mismo evento tampoco duplica.
        await using var third = NewContext();
        Assert.False(await new LateCommitReviewRepository(third, new FixedTenant(_tenantId)).RecordAsync(NewReview()));
    }

    private LateCommitReview NewReview() =>
        LateCommitReview
            .Record(
                _tenantId,
                _reservationId,
                PaymentReference.Create("PaymentApp", Guid.NewGuid()).Value,
                _sourceEventId,
                "Codes.PaymentOutcomeVerifier.NotImplemented",
                DateTime.UtcNow
            )
            .Value;
}
