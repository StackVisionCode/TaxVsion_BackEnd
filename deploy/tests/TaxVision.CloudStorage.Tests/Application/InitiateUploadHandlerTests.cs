using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fotos de perfil F0 — la carpeta Avatars no acepta subida directa de usuarios (staff ni portal).</summary>
public sealed class InitiateUploadHandlerTests
{
    private static InitiateUploadRequest AvatarRequest(OwnerType ownerType, Guid ownerId) =>
        new("me.webp", "image/webp", 2048, ownerType, ownerId, FolderType.Avatars, null);

    [Fact]
    public async Task Staff_cannot_upload_into_Avatars_even_for_a_User_owner()
    {
        var fixture = new Fixture();

        // Antes el staff podía crear archivos User de cualquier usuario; Avatars lo cierra.
        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null),
            AvatarRequest(OwnerType.User, Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.FolderReserved, result.Error);
        fixture.AssertNothingRegistered();
    }

    [Fact]
    public async Task Portal_cannot_upload_into_Avatars_even_for_its_own_customer()
    {
        var fixture = new Fixture();
        var customerId = Guid.NewGuid();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(true, customerId),
            AvatarRequest(OwnerType.Customer, customerId)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.FolderReserved, result.Error);
        fixture.AssertNothingRegistered();
    }

    [Fact]
    public async Task A_service_token_can_still_initiate_an_Avatars_upload()
    {
        var fixture = new Fixture();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null) { IsService = true },
            AvatarRequest(OwnerType.User, Guid.NewGuid())
        );

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Files.All(), f => f.FolderType == FolderType.Avatars);
    }

    private static InitiateUploadRequest TrackRequest(Guid meetingId) =>
        new(
            "Track_user_track.webm",
            "audio/webm",
            4096,
            OwnerType.Communication,
            meetingId,
            FolderType.RecordingTracks,
            null
        );

    [Fact]
    public async Task Staff_cannot_upload_into_RecordingTracks()
    {
        var fixture = new Fixture();

        // Solo el recorder sube pistas: una subida humana podría hacerse pasar por la voz de otro participante.
        var result = await fixture.InitiateAsync(new StorageActorScope(false, null), TrackRequest(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.FolderReserved, result.Error);
        fixture.AssertNothingRegistered();
    }

    [Fact]
    public async Task Portal_cannot_upload_into_RecordingTracks()
    {
        var fixture = new Fixture();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(true, Guid.NewGuid()),
            TrackRequest(Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.FolderReserved, result.Error);
        fixture.AssertNothingRegistered();
    }

    [Fact]
    public async Task A_service_token_can_initiate_a_RecordingTracks_upload()
    {
        var fixture = new Fixture();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null) { IsService = true },
            TrackRequest(Guid.NewGuid())
        );

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Files.All(), f => f.FolderType == FolderType.RecordingTracks);
    }

    [Fact]
    public async Task Other_folders_are_not_reserved()
    {
        var fixture = new Fixture();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null),
            new InitiateUploadRequest(
                "return.pdf",
                "application/pdf",
                2048,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026
            )
        );

        Assert.True(result.IsSuccess);
    }

    private static InitiateUploadRequest ChatAttachment(Guid? onBehalfOf) =>
        new("w2.png", "image/png", 2048, OwnerType.Communication, Guid.NewGuid(), FolderType.Other, null, onBehalfOf);

    [Fact]
    public async Task A_service_upload_on_behalf_of_a_person_records_that_person_as_the_author()
    {
        var fixture = new Fixture();
        var person = Guid.NewGuid();

        // E2E F8: con la cuenta de servicio como autora, el aviso "File under review" no le llegaba a nadie.
        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null) { IsService = true },
            ChatAttachment(person),
            actorId: Guid.NewGuid()
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(person, Assert.Single(fixture.Files.All()).CreatedBy);
    }

    [Fact]
    public async Task A_user_cannot_upload_on_behalf_of_someone_else()
    {
        var fixture = new Fixture();
        var me = Guid.NewGuid();

        var result = await fixture.InitiateAsync(
            new StorageActorScope(false, null),
            ChatAttachment(Guid.NewGuid()),
            actorId: me
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(me, Assert.Single(fixture.Files.All()).CreatedBy);
    }

    [Fact]
    public void FolderReserved_maps_to_403()
    {
        Assert.Equal(StatusCodes.Status403Forbidden, FileErrors.FolderReserved.ToHttpStatusCode());
        Assert.Equal("File.FolderReserved", FileErrors.FolderReserved.Code);
    }

    private sealed class Fixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public FakeFileObjectRepository Files { get; } = new();
        public FakeStorageLimitRepository Limits { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public TenantStorageLimit Limit { get; }

        public Fixture()
        {
            Limit = TenantStorageLimit.Create(TenantId, "starter", 100_000_000, 100_000_000);
            Limits.Seed(Limit);
        }

        public Task<BuildingBlocks.Results.Result<InitiatedUploadResponse>> InitiateAsync(
            StorageActorScope scope,
            InitiateUploadRequest request,
            Guid? actorId = null
        ) =>
            InitiateUploadHandler.Handle(
                new InitiateUploadCommand(
                    TenantId,
                    actorId ?? Guid.NewGuid(),
                    scope,
                    request,
                    new(null, null, "corr-1")
                ),
                Files,
                new FakeFolderRepository(),
                Limits,
                new FakeStorageAuditRepository(),
                new DefaultObjectKeyBuilder(),
                new SystemFolderProvisioner(new FakeFolderRepository()),
                new FakeObjectStorage(),
                Options.Create(new CloudStorageOptions()),
                new FakeUploadPolicyResolver(),
                new FakeSystemClock(DateTime.UtcNow),
                new FakeUnitOfWork(),
                Bus,
                CancellationToken.None
            );

        public void AssertNothingRegistered()
        {
            Assert.Empty(Files.All());
            Assert.Equal(0, Limit.ReservedBytes);
            Assert.Empty(Bus.Published);
        }
    }
}
