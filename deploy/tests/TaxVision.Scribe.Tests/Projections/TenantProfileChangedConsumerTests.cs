using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

/// <summary>Perfil de la oficina: nombre comercial y dirección postal del pie, ordenados por versión.</summary>
public sealed class TenantProfileChangedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OfficeAddressData Physical = new("1600 Main St", "Suite 2", "Miami", "FL", "33101", "US");

    [Fact]
    public async Task Handle_applies_display_name_and_postal_address_and_saves()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0));
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, 1, "  Acme DBA  ", Physical), repository, unitOfWork);

        Assert.Equal("Acme DBA", existing.DisplayName);
        Assert.Equal("Acme DBA", existing.SignatureName);
        Assert.Equal("Acme Tax", existing.Name);
        Assert.Equal("1600 Main St, Suite 2, Miami, FL 33101", existing.PostalAddress);
        Assert.Equal(1, existing.ProfileVersion);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_prefers_the_mailing_address_over_the_physical_one()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0));
        var mailing = new OfficeAddressData("PO Box 10", null, "Miami", "FL", "33102", "US");

        await HandleAsync(Event(tenantId, 1, null, Physical, mailing), repository, new FakeUnitOfWork());

        Assert.Equal("PO Box 10, Miami, FL 33102", existing.PostalAddress);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Handle_ignores_a_version_older_or_equal_to_the_applied_one(long version)
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0));
        existing.ApplyProfile(2, "Newest DBA", "1 Newest St, Miami", T0);
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, version, "Older DBA", null), repository, unitOfWork);

        Assert.Equal("Newest DBA", existing.DisplayName);
        Assert.Equal("1 Newest St, Miami", existing.PostalAddress);
        Assert.Equal(2, existing.ProfileVersion);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_TenantRowMissing_ThrowsDependencyNotReady()
    {
        var unitOfWork = new FakeUnitOfWork();

        var error = await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            HandleAsync(
                Event(Guid.NewGuid(), 1, "Acme DBA", Physical),
                new InMemoryTenantProfileRefRepository(),
                unitOfWork
            )
        );

        Assert.Equal("Tenant", error.Dependency);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Blank_values_clear_the_profile_and_the_signature_falls_back_to_the_name()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0));
        existing.ApplyProfile(1, "Acme DBA", "1600 Main St, Miami", T0);
        // Sin calle no hay dirección que imprimir: ToSingleLine devuelve null.
        var incomplete = new OfficeAddressData(null, null, "Miami", "FL", null, "US");

        await HandleAsync(Event(tenantId, 2, "   ", incomplete), repository, new FakeUnitOfWork());

        Assert.Null(existing.DisplayName);
        Assert.Null(existing.PostalAddress);
        Assert.Equal("Acme Tax", existing.SignatureName);
    }

    [Fact]
    public void A_rename_does_not_touch_the_profile_and_the_display_name_still_wins()
    {
        var profile = TenantProfileRef.Create(Guid.NewGuid(), "Acme Tax", "acme-tax", T0);
        profile.ApplyProfile(1, "Acme DBA", null, T0);

        profile.ApplyTenantUpdate("Acme Tax Group", T0, T0);

        Assert.Equal("Acme Tax Group", profile.Name);
        Assert.Equal("Acme DBA", profile.SignatureName);
        Assert.Equal(1, profile.ProfileVersion);
    }

    private static Task HandleAsync(
        TenantProfileChangedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantProfileChangedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantProfileRef>.Instance,
            CancellationToken.None
        );

    private static TenantProfileChangedIntegrationEvent Event(
        Guid tenantId,
        long version,
        string? displayName,
        OfficeAddressData? physical,
        OfficeAddressData? mailing = null
    ) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = version,
            UpdatedAtUtc = T0,
            DisplayName = displayName,
            PhysicalAddress = physical,
            MailingAddress = mailing,
        };

    private sealed class InMemoryTenantProfileRefRepository : ITenantProfileRefRepository
    {
        private readonly Dictionary<Guid, TenantProfileRef> _rows = [];

        public TenantProfileRef Seed(TenantProfileRef row)
        {
            _rows[row.TenantId] = row;
            return row;
        }

        public Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_rows.GetValueOrDefault(tenantId));

        public Task UpsertAsync(
            Guid tenantId,
            string name,
            string subDomain,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(_rows.Keys.ToList());
    }
}
