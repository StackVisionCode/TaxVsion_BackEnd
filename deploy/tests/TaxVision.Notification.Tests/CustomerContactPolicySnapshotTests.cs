using TaxVision.Notification.Application.Directory;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests;

public sealed class CustomerContactPolicySnapshotTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task A_customer_service_without_the_policy_fields_leaves_an_opted_out_email_untouched()
    {
        var existing = CustomerContactPolicyProjection.Create(TenantId, CustomerId, false, false, "Es");
        var repository = new Policies(existing);

        var applied = await CustomerContactPolicySnapshot.ApplyAsync(
            repository,
            repository,
            new RemoteCustomerContact(TenantId, CustomerId, "Client", "client@example.com", true),
            DateTime.UtcNow.AddMinutes(1),
            CancellationToken.None
        );

        Assert.False(applied);
        Assert.False(existing.EmailEnabled);
        Assert.Equal("Es", existing.Language);
    }

    [Fact]
    public async Task A_row_updated_after_the_snapshot_was_read_wins_over_the_snapshot()
    {
        var snapshotAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var existing = CustomerContactPolicyProjection.Create(TenantId, CustomerId, false, true, "Es");

        var policies = new Policies(existing);
        var applied = await CustomerContactPolicySnapshot.ApplyAsync(
            policies,
            policies,
            new RemoteCustomerContact(TenantId, CustomerId, "Client", "client@example.com", true, "En", true, true),
            snapshotAtUtc,
            CancellationToken.None
        );

        Assert.False(applied);
        Assert.False(existing.EmailEnabled);
    }

    [Fact]
    public async Task An_older_row_takes_the_snapshot_language_and_policy()
    {
        var existing = CustomerContactPolicyProjection.Create(TenantId, CustomerId, true, true);

        var policies = new Policies(existing);
        var applied = await CustomerContactPolicySnapshot.ApplyAsync(
            policies,
            policies,
            new RemoteCustomerContact(TenantId, CustomerId, "Client", "client@example.com", true, "Es", false, true),
            DateTime.UtcNow.AddMinutes(1),
            CancellationToken.None
        );

        Assert.True(applied);
        Assert.False(existing.EmailEnabled);
        Assert.Equal("Es", existing.Language);
    }

    [Fact]
    public async Task A_missing_row_is_created_from_the_snapshot()
    {
        var repository = new Policies(null);

        await CustomerContactPolicySnapshot.ApplyAsync(
            repository,
            repository,
            new RemoteCustomerContact(TenantId, CustomerId, "Client", "client@example.com", true, "Es", false, true),
            DateTime.UtcNow,
            CancellationToken.None
        );

        var created = Assert.Single(repository.Added);
        Assert.False(created.EmailEnabled);
        Assert.Equal("Es", created.Language);
    }

    private sealed class Policies(CustomerContactPolicyProjection? existing)
        : ICustomerContactPolicyRepository,
            ICustomerContactPolicySnapshotWriter
    {
        // Misma condición que el UPDATE real: solo si la fila es anterior al snapshot.
        public Task<bool> ReconcileIfOlderAsync(
            Guid tenantId,
            Guid customerId,
            bool emailEnabled,
            bool smsEnabled,
            string language,
            DateTime snapshotAtUtc,
            CancellationToken ct = default
        )
        {
            if (existing is null || existing.UpdatedAtUtc >= snapshotAtUtc)
                return Task.FromResult(false);
            existing.Reconcile(emailEnabled, smsEnabled, language);
            return Task.FromResult(true);
        }

        public List<CustomerContactPolicyProjection> Added { get; } = [];

        public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(existing);

        public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default)
        {
            Added.Add(row);
            return Task.CompletedTask;
        }
    }
}
