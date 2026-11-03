using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Settings.IntegrationEvents;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// La retención de la oficina (Cumplimiento, dueño Tenant) llega a Signature: ordenada por fecha, idempotente,
/// null vuelve a los años de Signature y sin fila de ajustes se crea con los defaults.
/// </summary>
public sealed class TenantRecordRetentionChangedConsumerTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static TenantRecordRetentionChangedIntegrationEvent Changed(int? years, DateTime at) =>
        new()
        {
            TenantId = Office,
            OfficeTenantId = Office,
            RetentionYears = years,
            UpdatedAtUtc = at,
        };

    private static Task HandleAsync(SettingsStore store, Uow uow, TenantRecordRetentionChangedIntegrationEvent evt) =>
        TenantRecordRetentionChangedConsumer.Handle(
            evt,
            store,
            new FakeAuditSecrets(),
            uow,
            new FixedCorrelation(),
            NullLogger<TenantSignatureSettings>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task The_office_retention_is_stored_on_the_existing_settings()
    {
        var store = SettingsStore.With(Office);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(12, At));

        Assert.Equal(12, store.Settings!.OfficeRetentionYears);
        Assert.Equal(At, store.Settings.OfficeRetentionUpdatedAtUtc);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task An_older_change_arriving_late_does_not_overwrite_a_newer_one()
    {
        var store = SettingsStore.With(Office);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(12, At));
        await HandleAsync(store, uow, Changed(5, At.AddHours(-1)));

        Assert.Equal(12, store.Settings!.OfficeRetentionYears);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task A_duplicate_delivery_is_not_saved_twice()
    {
        var store = SettingsStore.With(Office);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(12, At));
        await HandleAsync(store, uow, Changed(12, At));

        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task A_null_retention_returns_to_the_signature_years()
    {
        var store = SettingsStore.With(Office);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(12, At));
        await HandleAsync(store, uow, Changed(null, At.AddMinutes(5)));

        Assert.Null(store.Settings!.OfficeRetentionYears);
        Assert.Equal(RetentionYearsSource.Signature, store.Settings.RetentionSource);
        Assert.Equal(2, uow.Saves);
    }

    [Fact]
    public async Task Without_settings_row_one_is_created_with_defaults_and_the_office_retention()
    {
        var store = new SettingsStore(null);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(10, At));

        Assert.NotNull(store.Settings);
        Assert.Equal(Office, store.Settings!.TenantId);
        Assert.Equal(10, store.Settings.OfficeRetentionYears);
        Assert.False(store.Settings.Retention.AllowPurge);
        Assert.Equal(1, store.Adds);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task An_out_of_range_retention_is_dropped_without_saving()
    {
        var store = SettingsStore.With(Office);
        var uow = new Uow();

        await HandleAsync(store, uow, Changed(40, At));

        Assert.Null(store.Settings!.OfficeRetentionYears);
        Assert.Equal(0, uow.Saves);
    }

    private sealed class SettingsStore(TenantSignatureSettings? settings) : ITenantSignatureSettingsRepository
    {
        public TenantSignatureSettings? Settings { get; private set; } = settings;
        public int Adds { get; private set; }

        public static SettingsStore With(Guid tenantId) =>
            new(TenantSignatureSettings.CreateForNewTenant(tenantId, "audit-secret").Value);

        public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Settings?.TenantId == tenantId ? Settings : null);

        public Task AddAsync(TenantSignatureSettings toAdd, CancellationToken ct = default)
        {
            Settings = toAdd;
            Adds++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> ListTenantIdsWithPurgeEnabledAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeAuditSecrets : IAuditSecretFactory
    {
        public string GenerateProtected() => "protected-secret";
    }
}
