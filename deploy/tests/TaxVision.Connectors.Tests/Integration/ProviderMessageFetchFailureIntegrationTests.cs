using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Connectors.Domain.Sync;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (CONNECTORS_DB_CONNECTION o los user-secrets de la Api): el índice único
/// (AccountId, ProviderMessageId) deja una sola fila aunque dos pasadas registren el mismo fallo a la vez.
/// </summary>
public sealed class ProviderMessageFetchFailureIntegrationTests : IAsyncLifetime
{
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("CONNECTORS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("d5a2f07b-0550-4e76-a692-df921f63f798")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set CONNECTORS_DB_CONNECTION or the Connectors Api user-secrets to run this test."
        );

    private sealed class FixedTenant : ITenantContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private ConnectorsDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ConnectorsDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant()
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.ProviderMessageFetchFailures.Where(f => f.AccountId == _accountId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ConcurrentFailures_SameMessage_KeepSingleRow()
    {
        var now = DateTime.UtcNow;
        await using var first = NewContext();
        await using var second = NewContext();
        var firstRepository = new ProviderMessageFetchFailureRepository(first);
        var secondRepository = new ProviderMessageFetchFailureRepository(second);

        // Las dos pasadas leen "no hay fila" antes de que cualquiera guarde.
        Assert.Null(await firstRepository.GetAsync(_accountId, "m2"));
        Assert.Null(await secondRepository.GetAsync(_accountId, "m2"));
        await firstRepository.AddAsync(ProviderMessageFetchFailure.Create(_accountId, "m2", "HTTP 500", now).Value);
        await secondRepository.AddAsync(ProviderMessageFetchFailure.Create(_accountId, "m2", "HTTP 503", now).Value);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.IsAssignableFrom<DbUpdateException>(conflict.InnerException);

        // El reintento (Wolverine repite el mensaje) relee la fila y suma el intento.
        await using var retry = NewContext();
        var retryRepository = new ProviderMessageFetchFailureRepository(retry);
        var existing = await retryRepository.GetAsync(_accountId, "m2");
        Assert.NotNull(existing);
        existing.RegisterFailure("HTTP 503", now.AddSeconds(1));
        await retry.SaveChangesAsync();

        await using var check = NewContext();
        var row = await check.ProviderMessageFetchFailures.AsNoTracking().SingleAsync(f => f.AccountId == _accountId);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(ProviderMessageFetchFailureStatus.Pending, row.Status);
    }
}
