using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Application.Abstractions;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.Permissions;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Infrastructure.Security;

namespace TaxVision.Tenant.Tests.TestSupport;

internal sealed class InMemoryOfficeProfileRepository : IOfficeProfileRepository
{
    public List<OfficeProfile> Profiles { get; } = [];

    /// <summary>Oficinas que existen en el registro (las de plataforma nunca se listan).</summary>
    public HashSet<Guid> ExistingTenants { get; } = [];

    public Task<OfficeProfile?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Profiles.Find(p => p.TenantId == tenantId));

    public Task AddAsync(OfficeProfile profile, CancellationToken ct = default)
    {
        Profiles.Add(profile);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OfficeProfile>> ListSavedAsync(int skip, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OfficeProfile>>(
            Profiles.Where(p => p.UpdatedAtUtc != null).OrderBy(p => p.TenantId).Skip(skip).Take(take).ToList()
        );

    public Task<ImportCandidates> ClassifyImportCandidatesAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    )
    {
        var offices = tenantIds.Where(ExistingTenants.Contains).ToHashSet();
        var withProfile = offices.Where(id => Profiles.Any(p => p.TenantId == id)).ToHashSet();
        return Task.FromResult(new ImportCandidates(offices, withProfile));
    }
}

internal sealed class InMemoryOfficeComplianceRepository : IOfficeComplianceRepository
{
    public List<OfficeCompliance> Compliances { get; } = [];
    public List<EfinRevealRecord> RevealRecords { get; } = [];

    public Task<OfficeCompliance?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Compliances.Find(c => c.TenantId == tenantId));

    public Task AddAsync(OfficeCompliance compliance, CancellationToken ct = default)
    {
        Compliances.Add(compliance);
        return Task.CompletedTask;
    }

    public Task AddRevealRecordAsync(EfinRevealRecord record, CancellationToken ct = default)
    {
        RevealRecords.Add(record);
        return Task.CompletedTask;
    }

    /// <summary>Zona horaria de cada oficina para los candidatos del job (sin entrada = UTC).</summary>
    public Dictionary<Guid, string> TimeZones { get; } = [];

    public Task<IReadOnlyList<WispReminderCandidate>> ListWispReminderCandidatesAsync(
        DateOnly baseOnOrBefore,
        Guid? afterTenantId,
        int take,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<WispReminderCandidate>>(
            Compliances
                .Where(c => (c.WispLastReviewedOn ?? c.WispAdoptedOn) <= baseOnOrBefore)
                .Where(c => afterTenantId is null || c.TenantId.CompareTo(afterTenantId.Value) > 0)
                .OrderBy(c => c.TenantId)
                .Take(take)
                .Select(c => new WispReminderCandidate(c.TenantId, TimeZones.GetValueOrDefault(c.TenantId, "Etc/UTC")))
                .ToList()
        );
}

/// <summary>Billing de mentira: devuelve las páginas preparadas y anota cada pedido.</summary>
internal sealed class FakeBillingIssuerProfileClient : IBillingIssuerProfileClient
{
    public List<BillingIssuerProfile> Items { get; } = [];
    public List<(int Page, int Size)> Requests { get; } = [];
    public int? FailOnPage { get; set; }

    public Task<Result<BillingIssuerProfilePage>> GetPageAsync(int page, int size, CancellationToken ct = default)
    {
        Requests.Add((page, size));
        if (FailOnPage == page)
            return Task.FromResult(Result.Failure<BillingIssuerProfilePage>(BillingIssuerProfileErrors.Unavailable));

        var slice = Items.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(Result.Success(new BillingIssuerProfilePage(slice, page, size, Items.Count)));
    }
}

internal sealed class InMemoryUserPermissionsProjectionRepository : IUserPermissionsProjectionRepository
{
    public List<UserPermissionsProjection> Users { get; } = [];

    public UserPermissionsProjection Seed(Guid tenantId, Guid userId)
    {
        var user = UserPermissionsProjection.Create(tenantId, userId, 1, ["settings.manage"], []);
        Users.Add(user);
        return user;
    }

    public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Users.Find(u => u.TenantId == tenantId && u.UserId == userId));

    public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

/// <summary>Protector real de BuildingBlocks con una clave aleatoria por prueba (nunca una clave fija en el código).</summary>
internal static class TestEfinProtectors
{
    public static EfinProtector Create(out string masterKey, short keyVersion = 1, string? previousKey = null)
    {
        masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return Create(masterKey, keyVersion, previousKey, previousKey is null ? null : (short)(keyVersion - 1));
    }

    public static EfinProtector Create(
        string masterKey,
        short keyVersion,
        string? previousKey,
        short? previousVersion
    ) =>
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = masterKey,
                        MasterKeyVersion = keyVersion,
                        PreviousMasterKey = previousKey,
                        PreviousMasterKeyVersion = previousVersion,
                    }
                )
            )
        );
}
