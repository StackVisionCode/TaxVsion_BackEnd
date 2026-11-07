using System.Data.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using TaxVision.Tenant.Tests.TestSupport;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Mapeo real contra SQL Server (JSON de direcciones y horario, EFIN en columnas, token de versión).
/// Sin host ni Wolverine: un DbContext directo con la base de desarrollo (user-secrets de Tenant.Api),
/// todo dentro de una transacción que se revierte al final, así no deja filas.
/// </summary>
public sealed class OfficeProfilePersistenceTests : IAsyncLifetime
{
    private SqlConnection _connection = default!;
    private DbTransaction _transaction = default!;

    public async Task InitializeAsync()
    {
        var connectionString =
            TenantIntegrationDatabase.ConnectionString;
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
    public async Task A_full_profile_round_trips_and_a_second_update_replaces_the_json_parts()
    {
        var tenantId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var profile = OfficeProfile.CreateEmpty(tenantId);
            profile.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), DateTime.UtcNow);
            await new OfficeProfileRepository(db).AddAsync(profile);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var loaded = await new OfficeProfileRepository(db).GetAsync(tenantId);
            Assert.NotNull(loaded);
            Assert.Equal("78701-1234", loaded.PhysicalAddress!.PostalCode);
            Assert.Equal("PO Box 9", loaded.MailingAddress!.Line1);
            Assert.Equal([DayOfWeek.Monday, DayOfWeek.Saturday], loaded.BusinessHours.Select(h => h.Day));
            Assert.Equal(new TimeOnly(18, 0), loaded.BusinessHours[0].Closes);

            var unchanged = loaded.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), DateTime.UtcNow);
            Assert.False(unchanged.Value);

            loaded.Update(
                OfficeProfileDrafts.Full() with
                {
                    MailingSameAsPhysical = true,
                    BusinessHours = [new BusinessHoursDraft("Tuesday", "08:30", "12:00")],
                },
                Guid.NewGuid(),
                DateTime.UtcNow
            );
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var reloaded = await new OfficeProfileRepository(db).GetAsync(tenantId);
            Assert.Equal(2, reloaded!.Version);
            Assert.Null(reloaded.MailingAddress);
            Assert.Equal(DayOfWeek.Tuesday, Assert.Single(reloaded.BusinessHours).Day);
        }
    }

    [Fact]
    public async Task Two_saves_from_the_same_version_conflict_instead_of_sharing_a_version()
    {
        var tenantId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var profile = OfficeProfile.CreateEmpty(tenantId);
            profile.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), DateTime.UtcNow);
            await new OfficeProfileRepository(db).AddAsync(profile);
            await db.SaveChangesAsync();
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var fromFirst = await new OfficeProfileRepository(first).GetAsync(tenantId);
        var fromSecond = await new OfficeProfileRepository(second).GetAsync(tenantId);
        fromFirst!.Update(OfficeProfileDrafts.Full() with { LegalName = "First LLC" }, Guid.NewGuid(), DateTime.UtcNow);
        fromSecond!.Update(
            OfficeProfileDrafts.Full() with
            {
                LegalName = "Second LLC",
            },
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    [Fact]
    public async Task The_encrypted_efin_round_trips_and_clearing_it_leaves_no_parts()
    {
        var tenantId = Guid.NewGuid();
        var protector = TestEfinProtectors.Create(out _);
        var draft = new OfficeComplianceDraft(null, "2025-01-15", null, null, null, 7);
        await using (var db = NewContext())
        {
            var compliance = OfficeCompliance.CreateEmpty(tenantId);
            compliance.Update(
                draft,
                EfinChange.Replace(protector.Protect(Efin.Create("123456").Value)),
                Guid.NewGuid(),
                DateTime.UtcNow
            );
            await new OfficeComplianceRepository(db).AddAsync(compliance);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var loaded = await new OfficeComplianceRepository(db).GetAsync(tenantId);
            Assert.Equal("123456", protector.Reveal(loaded!.Efin!));
            Assert.Equal(new DateOnly(2025, 1, 15), loaded.WispAdoptedOn);

            var reveal = loaded.RecordEfinReveal(Guid.NewGuid(), DateTime.UtcNow);
            await new OfficeComplianceRepository(db).AddRevealRecordAsync(reveal.Value);
            loaded.Update(draft, EfinChange.Clear, Guid.NewGuid(), DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var reloaded = await new OfficeComplianceRepository(db).GetAsync(tenantId);
            Assert.Null(reloaded!.Efin);
            Assert.Equal(1, await db.EfinRevealRecords.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId));
        }
    }

    [Fact]
    public async Task Missing_profiles_are_only_existing_customer_offices_without_one()
    {
        var withoutProfile = DomainTenant.Create("No Profile Tax", $"np-{Guid.NewGuid():N}"[..20], "Etc/UTC", DateTime.UtcNow).Value;
        var withProfile = DomainTenant.Create("Profile Tax", $"wp-{Guid.NewGuid():N}"[..20], "Etc/UTC", DateTime.UtcNow).Value;
        await using (var db = NewContext())
        {
            db.Tenants.AddRange(withoutProfile, withProfile);
            var profile = OfficeProfile.CreateEmpty(withProfile.Id);
            profile.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), DateTime.UtcNow);
            db.OfficeProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var known = await new OfficeProfileRepository(db).ClassifyImportCandidatesAsync([
                withoutProfile.Id,
                withProfile.Id,
                Guid.NewGuid(),
                PlatformTenant.Id,
            ]);

            Assert.Equal(new HashSet<Guid> { withoutProfile.Id, withProfile.Id }, known.Offices.ToHashSet());
            Assert.Equal([withProfile.Id], known.WithProfile);
        }
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
