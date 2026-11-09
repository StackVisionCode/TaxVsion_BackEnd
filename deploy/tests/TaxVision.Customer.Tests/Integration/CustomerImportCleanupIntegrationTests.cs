using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Purga de importaciones contra el SQL Server real (user-secrets del Api o CUSTOMER_DB_CONNECTION).
/// La purga es cross-tenant: la retención de 100 años y los intentos sembrados en 1925 evitan tocar datos reales.
/// </summary>
public sealed class CustomerImportCleanupIntegrationTests : IAsyncLifetime
{
    private const int RetentionDays = 36500;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly FakeCloudStorage _cloudStorage = new();
    private readonly string _connectionString =
        CustomerIntegrationDatabase.ConnectionString;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class FakeCloudStorage : ICustomerImportCloudStorageClient
    {
        public List<Guid> Deleted { get; } = [];

        public Task<Result> UploadAsync(
            Guid tenantId,
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            Guid actorId,
            Wolverine.IMessageBus bus,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(Array.Empty<byte>()));

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Deleted.Add(fileId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    private CustomerDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenant)
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.CustomerImportRows.IgnoreQueryFilters().Where(r => r.TenantId == _tenant).ExecuteDeleteAsync();
        await db.CustomerImportAttempts.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Expired_attempt_with_rows_is_deleted_with_its_rows()
    {
        var expired = await SeedCompletedAttemptAsync(DateTime.UtcNow.AddDays(-RetentionDays - 1));

        await NewCleanup().PurgeOnceAsync(CancellationToken.None);

        await using var db = NewContext();
        Assert.False(await db.CustomerImportAttempts.IgnoreQueryFilters().AnyAsync(a => a.Id == expired));
        Assert.False(
            await db.CustomerImportRows.IgnoreQueryFilters().AnyAsync(r => r.CustomerImportAttemptId == expired)
        );
        Assert.Contains(expired, _cloudStorage.Deleted);
    }

    [Fact]
    public async Task Attempt_within_retention_keeps_its_rows()
    {
        var expired = await SeedCompletedAttemptAsync(DateTime.UtcNow.AddDays(-RetentionDays - 1));
        var recent = await SeedCompletedAttemptAsync(DateTime.UtcNow.AddDays(-RetentionDays + 1));

        await NewCleanup().PurgeOnceAsync(CancellationToken.None);

        await using var db = NewContext();
        Assert.False(await db.CustomerImportAttempts.IgnoreQueryFilters().AnyAsync(a => a.Id == expired));
        Assert.True(await db.CustomerImportAttempts.IgnoreQueryFilters().AnyAsync(a => a.Id == recent));
        Assert.Equal(
            2,
            await db.CustomerImportRows.IgnoreQueryFilters().CountAsync(r => r.CustomerImportAttemptId == recent)
        );
    }

    private CustomerImportCleanupHostedService NewCleanup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new FixedTenant(_tenant));
        services.AddDbContext<CustomerDbContext>(o => o.UseSqlServer(_connectionString));
        services.AddSingleton<ICustomerImportCloudStorageClient>(_cloudStorage);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["CustomerImport:ReportRetentionDays"] = $"{RetentionDays}" }
            )
            .Build();
        return new CustomerImportCleanupHostedService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            config,
            new StartedLifetime(),
            NullLogger<CustomerImportCleanupHostedService>.Instance
        );
    }

    private async Task<Guid> SeedCompletedAttemptAsync(DateTime createdAtUtc)
    {
        var attempt = CustomerImportAttempt
            .Create(
                _tenant,
                Guid.NewGuid(),
                $"cus8-{Guid.NewGuid():N}",
                DuplicateStrategy.Skip,
                ImportSourceKind.Csv,
                "cus8.csv"
            )
            .Value;
        Assert.True(attempt.Start().IsSuccess);
        Assert.True(attempt.MoveToApplying().IsSuccess);
        attempt.RecordSuccess(1, Guid.NewGuid(), "Row One");
        attempt.RecordFailed(2, "Row Two", "Customer.PersonalName", "First name is required.");
        Assert.True(attempt.Complete().IsSuccess);

        await using var db = NewContext();
        db.CustomerImportAttempts.Add(attempt);
        db.Entry(attempt).Property(a => a.CreatedAtUtc).CurrentValue = createdAtUtc;
        await db.SaveChangesAsync();
        return attempt.Id;
    }
}
