using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Consumers;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Flow completo de proyección del evento de preferencia de cobro:
/// alta en primera recepción, idempotencia por <c>RevisionTicks</c> y transiciones Automatic/Manual
/// que cambian <c>AllowsOffSessionCharge</c>.</summary>
public sealed class TenantBillingPreferenceProjectionConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task First_event_creates_the_projection()
    {
        var repo = new FakeProjectionRepository();
        var uow = new FakeUnitOfWork();

        await TenantBillingPreferenceProjectionConsumer.Handle(
            BuildEvent(mode: "Automatic", providerCode: "Stripe", hasMandate: true, revisionTicks: 100),
            repo,
            uow,
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(repo.Added);
        Assert.Equal(_tenantId, repo.Added!.TenantId);
        Assert.Equal(TenantBillingPreferenceProjection.ModeAutomatic, repo.Added.Mode);
        Assert.True(repo.Added.AllowsOffSessionCharge);
        Assert.Equal(100, repo.Added.RevisionTicks);
        Assert.Equal(1, uow.SaveCalls);
    }

    [Fact]
    public async Task Older_revision_is_dropped_by_apply_if_newer()
    {
        var existing = TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeAutomatic,
            "Stripe",
            hasActiveMandate: true,
            agreedAmountCents: 2900,
            agreedCurrency: "USD",
            agreedCadence: "Monthly",
            revisionTicks: 200,
            updatedAtUtc: DateTime.UtcNow
        );
        var repo = new FakeProjectionRepository(existing);

        await TenantBillingPreferenceProjectionConsumer.Handle(
            // Un evento más viejo (RevisionTicks menor) intenta pisar al Automatic activo con Manual.
            BuildEvent(mode: "Manual", providerCode: null, hasMandate: false, revisionTicks: 100),
            repo,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        // Mantiene el estado anterior porque el evento llegó fuera de orden.
        Assert.Equal(TenantBillingPreferenceProjection.ModeAutomatic, existing.Mode);
        Assert.True(existing.AllowsOffSessionCharge);
        Assert.Equal(200, existing.RevisionTicks);
    }

    [Fact]
    public async Task Newer_revision_updates_the_projection()
    {
        var existing = TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeManual,
            providerCode: null,
            hasActiveMandate: false,
            agreedAmountCents: null,
            agreedCurrency: null,
            agreedCadence: null,
            revisionTicks: 100,
            updatedAtUtc: DateTime.UtcNow
        );
        var repo = new FakeProjectionRepository(existing);

        await TenantBillingPreferenceProjectionConsumer.Handle(
            BuildEvent(
                mode: "Automatic",
                providerCode: "Stripe",
                hasMandate: true,
                revisionTicks: 300,
                amountCents: 2900
            ),
            repo,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantBillingPreferenceProjection.ModeAutomatic, existing.Mode);
        Assert.Equal("Stripe", existing.ProviderCode);
        Assert.True(existing.HasActiveMandate);
        Assert.Equal(300, existing.RevisionTicks);
        Assert.Equal(2900, existing.AgreedAmountCents);
        Assert.True(existing.AllowsOffSessionCharge);
    }

    [Fact]
    public async Task Switch_to_manual_without_mandate_disables_off_session()
    {
        var existing = TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeAutomatic,
            "Stripe",
            hasActiveMandate: true,
            agreedAmountCents: 2900,
            agreedCurrency: "USD",
            agreedCadence: "Monthly",
            revisionTicks: 100,
            updatedAtUtc: DateTime.UtcNow
        );
        var repo = new FakeProjectionRepository(existing);

        await TenantBillingPreferenceProjectionConsumer.Handle(
            BuildEvent(mode: "Manual", providerCode: null, hasMandate: false, revisionTicks: 200),
            repo,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantBillingPreferenceProjection.ModeManual, existing.Mode);
        Assert.False(existing.HasActiveMandate);
        Assert.False(existing.AllowsOffSessionCharge);
    }

    [Fact]
    public async Task Automatic_with_mandate_enables_off_session_charge()
    {
        var existing = TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeAutomatic,
            "Stripe",
            // mandate revocado — AllowsOffSessionCharge debería ser false hasta recibir el refresh.
            hasActiveMandate: false,
            agreedAmountCents: null,
            agreedCurrency: null,
            agreedCadence: null,
            revisionTicks: 100,
            updatedAtUtc: DateTime.UtcNow
        );
        var repo = new FakeProjectionRepository(existing);
        Assert.False(existing.AllowsOffSessionCharge);

        await TenantBillingPreferenceProjectionConsumer.Handle(
            BuildEvent(
                mode: "Automatic",
                providerCode: "Stripe",
                hasMandate: true,
                revisionTicks: 200,
                amountCents: 2900
            ),
            repo,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        Assert.True(existing.AllowsOffSessionCharge);
    }

    [Fact]
    public async Task Automatic_with_revoked_mandate_blocks_off_session_charge()
    {
        var repo = new FakeProjectionRepository();

        await TenantBillingPreferenceProjectionConsumer.Handle(
            BuildEvent(mode: "Automatic", providerCode: "Stripe", hasMandate: false, revisionTicks: 100),
            repo,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreferenceProjection>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(repo.Added);
        // Automatic + mandate revocado: la proyección NO autoriza cobro off-session.
        Assert.False(repo.Added!.AllowsOffSessionCharge);
    }

    private TenantBillingPreferenceChangedIntegrationEvent BuildEvent(
        string mode,
        string? providerCode,
        bool hasMandate,
        long revisionTicks,
        long? amountCents = null
    ) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "corr-proj",
            Mode = mode,
            ProviderCode = providerCode,
            HasActiveMandate = hasMandate,
            AgreedAmountCents = amountCents,
            AgreedCurrency = amountCents is null ? null : "USD",
            AgreedCadence = amountCents is null ? null : "Monthly",
            RevisionTicks = revisionTicks,
            ChangedAtUtc = DateTime.UtcNow,
        };

    private sealed class FakeProjectionRepository(TenantBillingPreferenceProjection? existing = null)
        : ITenantBillingPreferenceProjectionRepository
    {
        public TenantBillingPreferenceProjection? Added { get; private set; }

        public Task<TenantBillingPreferenceProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(existing);

        public Task AddAsync(TenantBillingPreferenceProjection projection, CancellationToken ct = default)
        {
            Added = projection;
            return Task.CompletedTask;
        }
    }
}
