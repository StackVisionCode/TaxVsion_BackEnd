using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Domain.Settings;
using Xunit;

namespace TaxVision.Signature.Tests.Application;

public sealed class EffectiveSignatureResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Uses_personal_default_when_own_signatures_are_allowed()
    {
        var personal = Profile(User);
        var office = Profile(null);
        var resolver = new EffectiveSignatureResolver(
            Settings(allowOwn: true),
            new FakeProfiles(personalDefault: personal, officeDefault: office)
        );

        var result = await resolver.ResolveAsync(Tenant, User);

        Assert.True(result.IsSuccess);
        Assert.Equal(personal.Id, result.Value.Id);
    }

    [Fact]
    public async Task Falls_back_to_office_when_user_has_no_personal_default()
    {
        var office = Profile(null);
        var resolver = new EffectiveSignatureResolver(
            Settings(allowOwn: true),
            new FakeProfiles(personalDefault: null, officeDefault: office)
        );

        var result = await resolver.ResolveAsync(Tenant, User);

        Assert.True(result.IsSuccess);
        Assert.Equal(office.Id, result.Value.Id);
    }

    [Fact]
    public async Task Uses_office_when_own_signatures_are_disabled_even_if_personal_exists()
    {
        var personal = Profile(User);
        var office = Profile(null);
        var resolver = new EffectiveSignatureResolver(
            Settings(allowOwn: false),
            new FakeProfiles(personalDefault: personal, officeDefault: office)
        );

        var result = await resolver.ResolveAsync(Tenant, User);

        Assert.True(result.IsSuccess);
        Assert.Equal(office.Id, result.Value.Id);
    }

    [Fact]
    public async Task Fails_when_no_signature_is_configured()
    {
        var resolver = new EffectiveSignatureResolver(
            Settings(allowOwn: true),
            new FakeProfiles(personalDefault: null, officeDefault: null)
        );

        var result = await resolver.ResolveAsync(Tenant, User);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.NoEffective", result.Error.Code);
    }

    private static SignatureProfile Profile(Guid? owner) =>
        SignatureProfile.Create(Tenant, User, owner, "Sig", Guid.NewGuid(), 100, 50).Value;

    private static FakeSettings Settings(bool allowOwn)
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Tenant, "secret").Value;
        if (!allowOwn)
            settings.DisableEmployeeOwnSignature();
        return new FakeSettings(settings);
    }

    private sealed class FakeSettings(TenantSignatureSettings settings) : ITenantSignatureSettingsRepository
    {
        public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantSignatureSettings?>(settings);

        public Task AddAsync(TenantSignatureSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeProfiles(SignatureProfile? personalDefault, SignatureProfile? officeDefault)
        : ISignatureProfileRepository
    {
        public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<SignatureProfile?>(null);

        public Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
            Guid tenantId,
            Guid userId,
            bool includePersonal,
            bool includeArchived,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureProfile>>([]);

        public Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
            Guid tenantId,
            Guid? ownerUserId,
            bool includeArchived,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureProfile>>([]);

        public Task<SignatureProfile?> GetDefaultAsync(
            Guid tenantId,
            Guid? ownerUserId,
            CancellationToken ct = default
        ) => Task.FromResult(ownerUserId is null ? officeDefault : personalDefault);

        public Task AddAsync(SignatureProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public void Remove(SignatureProfile profile) { }
    }
}
