using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Projections.TenantEvents;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>Perfil de la oficina: el From usa el nombre comercial; una versión vieja no lo revierte.</summary>
public sealed class TenantProfileChangedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Profile_sets_the_business_name_and_saves_once()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(TenantDirectoryEntry.Create(tenantId, "Acme Tax", "acme-tax", T0));
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, 1, "  Acme DBA  "), repository, unitOfWork);

        Assert.Equal("Acme DBA", entry.DisplayName);
        Assert.Equal("Acme DBA", entry.SenderName);
        Assert.Equal("Acme Tax", entry.Name);
        Assert.Equal(1, entry.ProfileVersion);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Older_or_equal_version_is_ignored(long version)
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(TenantDirectoryEntry.Create(tenantId, "Acme Tax", "acme-tax", T0));
        entry.ApplyProfile(2, "Newest DBA", T0);
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, version, "Older DBA"), repository, unitOfWork);

        Assert.Equal("Newest DBA", entry.DisplayName);
        Assert.Equal(2, entry.ProfileVersion);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_TenantRowMissing_ThrowsDependencyNotReady()
    {
        var unitOfWork = new FakeUnitOfWork();

        var error = await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            HandleAsync(Event(Guid.NewGuid(), 1, "Acme DBA"), new InMemoryTenantDirectoryRepository(), unitOfWork)
        );

        Assert.Equal("Tenant", error.Dependency);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Blank_business_name_clears_it_and_the_sender_falls_back_to_the_name()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(TenantDirectoryEntry.Create(tenantId, "Acme Tax", "acme-tax", T0));
        entry.ApplyProfile(1, "Acme DBA", T0);

        await HandleAsync(Event(tenantId, 2, "   "), repository, new FakeUnitOfWork());

        Assert.Null(entry.DisplayName);
        Assert.Equal("Acme Tax", entry.SenderName);
    }

    [Fact]
    public void A_rename_keeps_the_business_name_as_the_sender()
    {
        var entry = TenantDirectoryEntry.Create(Guid.NewGuid(), "Acme Tax", "acme-tax", T0);
        entry.ApplyProfile(1, "Acme DBA", T0);

        entry.ApplyTenantUpdate("Acme Tax Group", T0, T0);

        Assert.Equal("Acme Tax Group", entry.Name);
        Assert.Equal("Acme DBA", entry.SenderName);
    }

    private static Task HandleAsync(
        TenantProfileChangedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantProfileChangedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantDirectoryEntry>.Instance,
            CancellationToken.None
        );

    private static TenantProfileChangedIntegrationEvent Event(Guid tenantId, long version, string? displayName) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = version,
            UpdatedAtUtc = T0,
            DisplayName = displayName,
        };

    private sealed class InMemoryTenantDirectoryRepository : ITenantDirectoryRepository
    {
        private readonly Dictionary<Guid, TenantDirectoryEntry> _rows = [];

        public TenantDirectoryEntry Seed(TenantDirectoryEntry entry)
        {
            _rows[entry.TenantId] = entry;
            return entry;
        }

        public Task<TenantDirectoryEntry?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_rows.GetValueOrDefault(tenantId));

        public Task UpsertAsync(
            Guid tenantId,
            string name,
            string subDomain,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(_rows.Keys.ToHashSet());
    }
}
