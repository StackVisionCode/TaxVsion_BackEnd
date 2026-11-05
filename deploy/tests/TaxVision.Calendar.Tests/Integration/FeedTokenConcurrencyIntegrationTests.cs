using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Feeds.Commands;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Doble clic en «Regenerate feed URL»: dos emisiones a la vez no pueden dejar dos tokens activos.
/// Con SQL real (CALENDAR_TEST_DB_CONNECTION o los user-secrets del Api); limpia su tenant al final.
/// </summary>
public sealed class FeedTokenConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string ApiUserSecretsId = "b7f4a1d2-9c3e-4a58-8f21-6d0e5b3c74a9";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.CalendarFeedTokens.IgnoreQueryFilters().Where(t => t.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DoubleIssueInParallel_LeavesOneActive()
    {
        var barrier = new TwoPartyBarrier();

        var outcomes = await Task.WhenAll(IssueAsync(barrier), IssueAsync(barrier));

        await using var check = CreateContext();
        var active = await check
            .CalendarFeedTokens.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == _tenant && t.UserId == _user && t.RevokedAtUtc == null);
        Assert.Equal(1, active);
        Assert.Single(outcomes, o => o is ConflictException);
    }

    /// <summary>Devuelve null si emitió, o la excepción con la que falló.</summary>
    private async Task<Exception?> IssueAsync(TwoPartyBarrier barrier)
    {
        await using var context = CreateContext();
        try
        {
            await IssueFeedTokenHandler.Handle(
                new IssueFeedTokenCommand(_tenant, _user),
                new CalendarFeedTokenRepository(context),
                new NoCache(),
                new BarrierUnitOfWork(context, barrier),
                CancellationToken.None
            );
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private CalendarDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CalendarDbContext>().UseSqlServer(ConnectionString()).Options;
        return new CalendarDbContext(options, new FixedTenant(_tenant));
    }

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("CALENDAR_TEST_DB_CONNECTION")
        ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
        ?? throw new InvalidOperationException("No Calendar test database configured.");

    /// <summary>Los dos lados leen antes de que cualquiera guarde: reproduce la carrera sin azar.</summary>
    private sealed class TwoPartyBarrier
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 2)
                _both.SetResult();
            return _both.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class BarrierUnitOfWork(CalendarDbContext context, TwoPartyBarrier barrier) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            await barrier.ArriveAsync();
            return await context.SaveChangesAsync(ct);
        }
    }

    private sealed class NoCache : ICalendarFeedCache
    {
        public Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(string tokenHashHex, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;

        public bool HasTenant => true;

        public void SetTenant(Guid value) { }
    }
}
