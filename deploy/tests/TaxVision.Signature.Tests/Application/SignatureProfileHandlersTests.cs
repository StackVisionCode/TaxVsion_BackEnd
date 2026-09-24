using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Profiles.Commands.Create;
using TaxVision.Signature.Application.Profiles.Commands.Delete;
using TaxVision.Signature.Application.Profiles.Commands.Rename;
using TaxVision.Signature.Application.Profiles.Commands.SetDefault;
using TaxVision.Signature.Application.Profiles.Queries.List;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Domain.Settings;
using Xunit;

namespace TaxVision.Signature.Tests.Application;

public sealed class SignatureProfileHandlersTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid OtherUser = Guid.NewGuid();

    /// <summary>PNG mínimo válido (firma + IHDR + ancho/alto) para pasar SignatureImageValidator.</summary>
    private static byte[] MinimalPng(int width = 100, int height = 50) =>
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A, // firma PNG
            0x00,
            0x00,
            0x00,
            0x0D, // len
            (byte)'I',
            (byte)'H',
            (byte)'D',
            (byte)'R',
            (byte)(width >> 24),
            (byte)(width >> 16),
            (byte)(width >> 8),
            (byte)width,
            (byte)(height >> 24),
            (byte)(height >> 16),
            (byte)(height >> 8),
            (byte)height,
        ];

    [Fact]
    public async Task Create_first_profile_becomes_default_and_uploads_image()
    {
        var repo = new FakeRepository();
        var storage = new FakeCloudStorage();

        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: false,
                OwnerUserId: User,
                "Blue",
                MinimalPng()
            ),
            repo,
            Settings(),
            storage,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsDefault);
        Assert.Equal(1, storage.Uploads);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Create_second_profile_is_not_default()
    {
        var repo = new FakeRepository();
        repo.Seed(MakeProfile(User, isDefault: true));

        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: false,
                OwnerUserId: User,
                "Second",
                MinimalPng()
            ),
            repo,
            Settings(),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsDefault);
    }

    [Fact]
    public async Task Create_rejects_when_the_scope_is_at_the_limit()
    {
        var repo = new FakeRepository();
        for (var i = 0; i < SignatureProfile.MaxActiveProfilesPerScope; i++)
            repo.Seed(MakeProfile(User, isDefault: i == 0));

        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: false,
                OwnerUserId: User,
                "Extra",
                MinimalPng()
            ),
            repo,
            Settings(),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.LimitReached", result.Error.Code);
    }

    [Fact]
    public async Task Create_office_signature_requires_admin()
    {
        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: false,
                OwnerUserId: null,
                "Office",
                MinimalPng()
            ),
            new FakeRepository(),
            Settings(),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Create_rejects_a_non_png_image()
    {
        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(Tenant, User, ActorIsAdmin: false, OwnerUserId: User, "Bad", [1, 2, 3]),
            new FakeRepository(),
            Settings(),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.StartsWith("Signature.Image.", result.Error.Code);
    }

    [Fact]
    public async Task SetDefault_unsets_the_previous_default_in_scope()
    {
        var repo = new FakeRepository();
        var first = MakeProfile(User, isDefault: true);
        var second = MakeProfile(User, isDefault: false);
        repo.Seed(first);
        repo.Seed(second);

        var result = await SetDefaultSignatureProfileHandler.Handle(
            new SetDefaultSignatureProfileCommand(Tenant, second.Id, User, ActorIsAdmin: false),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(second.IsDefault);
        Assert.False(first.IsDefault);
    }

    [Fact]
    public async Task Rename_returns_not_found_when_missing()
    {
        var result = await RenameSignatureProfileHandler.Handle(
            new RenameSignatureProfileCommand(Tenant, Guid.NewGuid(), User, ActorIsAdmin: false, "X"),
            new FakeRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Cannot_manage_another_users_profile()
    {
        var repo = new FakeRepository();
        var others = MakeProfile(OtherUser, isDefault: false);
        repo.Seed(others);

        var result = await RenameSignatureProfileHandler.Handle(
            new RenameSignatureProfileCommand(Tenant, others.Id, User, ActorIsAdmin: false, "Mine now"),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Delete_removes_the_profile()
    {
        var repo = new FakeRepository();
        var profile = MakeProfile(User, isDefault: false);
        repo.Seed(profile);

        var result = await DeleteSignatureProfileHandler.Handle(
            new DeleteSignatureProfileCommand(Tenant, profile.Id, User, ActorIsAdmin: false),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task Create_personal_is_blocked_for_employee_when_own_signatures_are_disabled()
    {
        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: false,
                OwnerUserId: User,
                "Mine",
                MinimalPng()
            ),
            new FakeRepository(),
            Settings(allowOwn: false),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.OwnSignatureDisabled", result.Error.Code);
    }

    [Fact]
    public async Task Admin_can_create_personal_even_when_own_signatures_are_disabled()
    {
        var result = await CreateSignatureProfileHandler.Handle(
            new CreateSignatureProfileCommand(
                Tenant,
                User,
                ActorIsAdmin: true,
                OwnerUserId: User,
                "Admin mine",
                MinimalPng()
            ),
            new FakeRepository(),
            Settings(allowOwn: false),
            new FakeCloudStorage(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task List_hides_personal_signatures_for_employee_when_own_signatures_are_disabled()
    {
        var repo = new FakeRepository();
        var personal = MakeProfile(User, isDefault: true);
        var office = MakeProfile(owner: null, isDefault: true);
        repo.Seed(personal);
        repo.Seed(office);

        var result = await ListSignatureProfilesHandler.Handle(
            new ListSignatureProfilesQuery(Tenant, User, ActorIsAdmin: false, IncludeArchived: false),
            repo,
            Settings(allowOwn: false),
            CancellationToken.None
        );

        Assert.False(result.CanManageOwnSignature);
        Assert.Single(result.Profiles);
        Assert.Equal(office.Id, result.Profiles[0].Id);
    }

    [Fact]
    public async Task List_includes_personal_signatures_when_own_signatures_are_allowed()
    {
        var repo = new FakeRepository();
        repo.Seed(MakeProfile(User, isDefault: true));
        repo.Seed(MakeProfile(owner: null, isDefault: true));

        var result = await ListSignatureProfilesHandler.Handle(
            new ListSignatureProfilesQuery(Tenant, User, ActorIsAdmin: false, IncludeArchived: false),
            repo,
            Settings(allowOwn: true),
            CancellationToken.None
        );

        Assert.True(result.CanManageOwnSignature);
        Assert.Equal(2, result.Profiles.Count);
    }

    private static SignatureProfile MakeProfile(Guid? owner, bool isDefault)
    {
        var profile = SignatureProfile.Create(Tenant, User, owner, "Sig", Guid.NewGuid(), 100, 50).Value;
        if (isDefault)
            profile.MarkDefault();
        return profile;
    }

    private static FakeSettings Settings(bool allowOwn = true)
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

    private sealed class FakeRepository : ISignatureProfileRepository
    {
        private readonly List<SignatureProfile> store = [];
        public IReadOnlyList<SignatureProfile> Items => store;

        public void Seed(SignatureProfile profile) => store.Add(profile);

        public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(store.FirstOrDefault(p => p.TenantId == tenantId && p.Id == id));

        public Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
            Guid tenantId,
            Guid userId,
            bool includePersonal,
            bool includeArchived,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<SignatureProfile>>(
                store
                    .Where(p =>
                        p.TenantId == tenantId
                        && (p.OwnerUserId == null || (includePersonal && p.OwnerUserId == userId))
                        && (includeArchived || !p.IsArchived)
                    )
                    .ToList()
            );

        public Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
            Guid tenantId,
            Guid? ownerUserId,
            bool includeArchived,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<SignatureProfile>>(
                store
                    .Where(p =>
                        p.TenantId == tenantId && p.OwnerUserId == ownerUserId && (includeArchived || !p.IsArchived)
                    )
                    .ToList()
            );

        public Task<SignatureProfile?> GetDefaultAsync(
            Guid tenantId,
            Guid? ownerUserId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                store.FirstOrDefault(p =>
                    p.TenantId == tenantId && p.OwnerUserId == ownerUserId && p.IsDefault && !p.IsArchived
                )
            );

        public Task AddAsync(SignatureProfile profile, CancellationToken ct = default)
        {
            store.Add(profile);
            return Task.CompletedTask;
        }

        public void Remove(SignatureProfile profile) => store.Remove(profile);
    }

    private sealed class FakeCloudStorage : ISignatureCloudStorageClient
    {
        public int Uploads { get; private set; }

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(Array.Empty<byte>()));

        public Task<Result<Guid>> UploadAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default)
        {
            Uploads++;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        public Task<Result<string>> CreateDownloadShareLinkAsync(
            Guid tenantId,
            Guid fileId,
            IReadOnlyList<string> recipientEmails,
            DateTime expiresAtUtc,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(string.Empty));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
