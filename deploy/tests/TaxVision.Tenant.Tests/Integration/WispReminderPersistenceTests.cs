using System.Data.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Consulta de candidatos del job del WISP y token de concurrencia del aviso, contra SQL Server real (base de
/// desarrollo, user-secrets de Tenant.Api), dentro de una transacción que se revierte al final.
/// </summary>
public sealed class WispReminderPersistenceTests : IAsyncLifetime
{
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private SqlConnection _connection = default!;
    private DbTransaction _transaction = default!;

    public async Task InitializeAsync()
    {
        var connectionString = TenantIntegrationDatabase.ConnectionString;
        _connection = new SqlConnection(connectionString);
        await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Candidates_are_active_offices_whose_wisp_base_is_old_enough_with_their_time_zone()
    {
        var reviewed = await SeedOfficeAsync("America/Chicago", adopted: "2023-01-10", reviewed: "2025-11-20");
        var adoptedOnly = await SeedOfficeAsync("Etc/UTC", adopted: "2025-10-01", reviewed: null);
        var tooRecent = await SeedOfficeAsync("Etc/UTC", adopted: "2023-01-10", reviewed: "2026-03-01");
        var withoutWisp = await SeedOfficeAsync("Etc/UTC", adopted: null, reviewed: null);
        var suspended = await SeedOfficeAsync("Etc/UTC", adopted: "2025-10-01", reviewed: null, suspend: true);

        await using var db = NewContext();
        var candidates = await new OfficeComplianceRepository(db).ListWispReminderCandidatesAsync(
            new DateOnly(2025, 12, 1),
            afterTenantId: null,
            take: 10_000
        );

        var byTenant = candidates.ToDictionary(c => c.TenantId);
        Assert.Equal("America/Chicago", byTenant[reviewed].TimeZoneId);
        Assert.Equal("Etc/UTC", byTenant[adoptedOnly].TimeZoneId);
        Assert.DoesNotContain(tooRecent, byTenant.Keys);
        Assert.DoesNotContain(withoutWisp, byTenant.Keys);
        Assert.DoesNotContain(suspended, byTenant.Keys);
    }

    [Fact]
    public async Task Candidates_page_by_tenant_id_without_repeating_or_skipping()
    {
        var mine = new HashSet<Guid>();
        for (var i = 0; i < 5; i++)
            mine.Add(await SeedOfficeAsync("Etc/UTC", adopted: "2020-01-01", reviewed: null));

        await using var db = NewContext();
        var repository = new OfficeComplianceRepository(db);
        var seen = new List<Guid>();
        Guid? after = null;
        while (true)
        {
            var page = await repository.ListWispReminderCandidatesAsync(new DateOnly(2020, 1, 1), after, take: 2);
            seen.AddRange(page.Select(c => c.TenantId));
            if (page.Count < 2)
                break;
            after = page[^1].TenantId;
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Subset(seen.ToHashSet(), mine);
    }

    [Fact]
    public async Task Two_replicas_registering_the_same_reminder_conflict_and_only_one_saves()
    {
        var tenantId = await SeedOfficeAsync("Etc/UTC", adopted: "2025-12-01", reviewed: null);
        var today = new DateOnly(2026, 12, 2);

        await using var first = NewContext();
        await using var second = NewContext();
        var fromFirst = await new OfficeComplianceRepository(first).GetAsync(tenantId);
        var fromSecond = await new OfficeComplianceRepository(second).GetAsync(tenantId);
        Assert.NotNull(fromFirst!.RegisterWispReviewReminder(today));
        Assert.NotNull(fromSecond!.RegisterWispReviewReminder(today));

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var check = NewContext();
        var stored = await new OfficeComplianceRepository(check).GetAsync(tenantId);
        Assert.Equal(new DateOnly(2026, 12, 1), stored!.WispReminderDueOn);
        Assert.Equal(WispReviewReminderStage.Overdue, stored.WispReminderStage);
        Assert.Equal(today, stored.WispReminderSentOn);
        Assert.Null(stored.RegisterWispReviewReminder(today));
    }

    private async Task<Guid> SeedOfficeAsync(string timeZoneId, string? adopted, string? reviewed, bool suspend = false)
    {
        var tenant = DomainTenant.Create("Wisp Tax", $"wr-{Guid.NewGuid():N}"[..20], timeZoneId, DateTime.UtcNow).Value;
        if (suspend)
            tenant.Suspend();

        var compliance = OfficeCompliance.CreateEmpty(tenant.Id);
        compliance.Update(
            new OfficeComplianceDraft(null, adopted, reviewed, null, null, 7),
            EfinChange.Keep,
            Guid.NewGuid(),
            SavedAt
        );

        await using var db = NewContext();
        db.Tenants.Add(tenant);
        db.OfficeCompliances.Add(compliance);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private TenantDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(_connection).Options;
        var db = new TenantDbContext(options, new NoTenantContext());
        db.Database.UseTransaction(_transaction);
        return db;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("No ambient tenant in this test.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) => throw new InvalidOperationException("No ambient tenant in this test.");
    }
}
