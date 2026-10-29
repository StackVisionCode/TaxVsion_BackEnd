using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// X-5 — FilesDeletionRequestedConsumer: verifica dueño/carpeta/legal-hold/existencia, borra blando primero
/// y publica FileDeleted; salta publicando FileDeletionSkipped cuando algo no coincide.
/// </summary>
public sealed class FilesDeletionRequestedConsumerTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static FileObject ClientFormsFile(Guid tenantId, OwnerType ownerType, Guid ownerId) =>
        FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                ownerType,
                ownerId,
                FolderType.ClientForms,
                null,
                ObjectKey.Create($"tenants/{tenantId:N}/task/{ownerId:N}/client-forms/{Guid.NewGuid():N}.pdf").Value,
                "form.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                Now,
                Now.AddHours(1)
            )
            .Value;

    private static FileDeletionTarget Target(Guid fileId, string ownerType, Guid ownerId) =>
        new()
        {
            FileId = fileId,
            ExpectedOwnerType = ownerType,
            ExpectedOwnerId = ownerId,
            ExpectedFolderType = "ClientForms",
        };

    private static FilesDeletionRequestedIntegrationEvent Message(Guid tenantId, params FileDeletionTarget[] files) =>
        new()
        {
            TenantId = tenantId,
            SourceService = "tasks.client_forms",
            SourceReferenceId = Guid.NewGuid(),
            Reason = "retention",
            Files = files,
            CorrelationId = "corr-1",
        };

    private static Task Run(
        FilesDeletionRequestedIntegrationEvent message,
        FakeFileObjectRepository files,
        FakeMessageBus bus,
        FakeStorageAuditRepository? audit = null
    ) =>
        FilesDeletionRequestedConsumer.Handle(
            message,
            files,
            audit ?? new FakeStorageAuditRepository(),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(Now),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<FileDeletionSkippedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Owner_and_folder_match_soft_deletes_and_announces_file_deleted()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = ClientFormsFile(tenantId, OwnerType.Customer, customerId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        await Run(Message(tenantId, Target(file.Id, "Customer", customerId)), files, bus);

        Assert.Equal(FileStatus.SoftDeleted, file.Status);
        var deleted = Assert.Single(bus.Published.OfType<FileDeletedIntegrationEvent>());
        Assert.Equal(file.Id, deleted.FileId);
        Assert.Empty(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
    }

    [Fact]
    public async Task Owner_mismatch_is_skipped_and_not_deleted()
    {
        var tenantId = Guid.NewGuid();
        var file = ClientFormsFile(tenantId, OwnerType.FormParticipant, Guid.NewGuid());
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        // El owner esperado (el actual, reasignado a Customer) ya no coincide con el del archivo.
        await Run(Message(tenantId, Target(file.Id, "Customer", Guid.NewGuid())), files, bus);

        Assert.NotEqual(FileStatus.SoftDeleted, file.Status);
        var skipped = Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
        Assert.Equal("owner_mismatch", skipped.Reason);
        Assert.Empty(bus.Published.OfType<FileDeletedIntegrationEvent>());
    }

    [Fact]
    public async Task Legal_hold_is_skipped()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = ClientFormsFile(tenantId, OwnerType.Customer, customerId);
        file.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        await Run(Message(tenantId, Target(file.Id, "Customer", customerId)), files, bus);

        Assert.NotEqual(FileStatus.SoftDeleted, file.Status);
        var skipped = Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
        Assert.Equal("legal_hold", skipped.Reason);
    }

    [Fact]
    public async Task Missing_file_is_skipped_as_not_found()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        var bus = new FakeMessageBus();

        await Run(Message(tenantId, Target(Guid.NewGuid(), "Customer", Guid.NewGuid())), files, bus);

        var skipped = Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
        Assert.Equal("not_found", skipped.Reason);
    }

    [Fact]
    public async Task Folder_mismatch_is_skipped()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerId,
                FolderType.Documents,
                2026,
                ObjectKey
                    .Create($"tenants/{tenantId:N}/customer/{customerId:N}/documents/2026/{Guid.NewGuid():N}.pdf")
                    .Value,
                "doc.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                Now,
                Now.AddHours(1)
            )
            .Value;
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        await Run(Message(tenantId, Target(file.Id, "Customer", customerId)), files, bus);

        Assert.NotEqual(FileStatus.SoftDeleted, file.Status);
        var skipped = Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
        Assert.Equal("folder_mismatch", skipped.Reason);
    }

    [Fact]
    public async Task Already_soft_deleted_is_idempotent_and_not_reannounced()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = ClientFormsFile(tenantId, OwnerType.Customer, customerId);
        file.SoftDelete(Now, TimeSpan.FromDays(30));
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        await Run(Message(tenantId, Target(file.Id, "Customer", customerId)), files, bus);

        Assert.Empty(bus.Published.OfType<FileDeletedIntegrationEvent>());
        var skipped = Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
        Assert.Equal("already_deleted", skipped.Reason);
    }

    [Fact]
    public async Task Batch_deletes_matching_and_skips_mismatching()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ok = ClientFormsFile(tenantId, OwnerType.Customer, customerId);
        var mismatch = ClientFormsFile(tenantId, OwnerType.FormParticipant, Guid.NewGuid());
        var files = new FakeFileObjectRepository();
        files.Seed(ok);
        files.Seed(mismatch);
        var bus = new FakeMessageBus();

        await Run(
            Message(tenantId, Target(ok.Id, "Customer", customerId), Target(mismatch.Id, "Customer", Guid.NewGuid())),
            files,
            bus
        );

        Assert.Equal(FileStatus.SoftDeleted, ok.Status);
        Assert.NotEqual(FileStatus.SoftDeleted, mismatch.Status);
        Assert.Single(bus.Published.OfType<FileDeletedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
    }

    // Fotos de perfil: Auth (User) y Customer borran la foto reemplazada por evento, sin DELETE con token de servicio.
    [Theory]
    [InlineData(OwnerType.User, "User")]
    [InlineData(OwnerType.Customer, "Customer")]
    public async Task Replaced_avatar_is_soft_deleted_when_owner_and_folder_match(OwnerType ownerType, string expected)
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var avatar = AvatarTestFiles.Avatar(tenantId, ownerType, ownerId);
        var files = new FakeFileObjectRepository();
        files.Seed(avatar);
        var bus = new FakeMessageBus();

        await Run(AvatarMessage(tenantId, AvatarTarget(avatar.Id, expected, ownerId)), files, bus);

        Assert.Equal(FileStatus.SoftDeleted, avatar.Status);
        Assert.Equal(avatar.Id, Assert.Single(bus.Published.OfType<FileDeletedIntegrationEvent>()).FileId);
        Assert.Empty(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>());
    }

    [Fact]
    public async Task Avatar_of_another_user_is_skipped_as_owner_mismatch()
    {
        var tenantId = Guid.NewGuid();
        var avatar = AvatarTestFiles.Avatar(tenantId, OwnerType.User, Guid.NewGuid());
        var files = new FakeFileObjectRepository();
        files.Seed(avatar);
        var bus = new FakeMessageBus();

        await Run(AvatarMessage(tenantId, AvatarTarget(avatar.Id, "User", Guid.NewGuid())), files, bus);

        Assert.Equal(FileStatus.Available, avatar.Status);
        Assert.Equal(
            "owner_mismatch",
            Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>()).Reason
        );
    }

    [Fact]
    public async Task Avatar_deletion_cannot_reach_a_document_of_the_same_owner()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var document = AvatarTestFiles.Document(tenantId, customerId);
        var files = new FakeFileObjectRepository();
        files.Seed(document);
        var bus = new FakeMessageBus();

        await Run(AvatarMessage(tenantId, AvatarTarget(document.Id, "Customer", customerId)), files, bus);

        Assert.Equal(FileStatus.Available, document.Status);
        Assert.Equal(
            "folder_mismatch",
            Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>()).Reason
        );
    }

    [Fact]
    public async Task Avatar_of_another_tenant_is_not_found()
    {
        var ownerId = Guid.NewGuid();
        var avatar = AvatarTestFiles.Avatar(Guid.NewGuid(), OwnerType.User, ownerId);
        var files = new FakeFileObjectRepository();
        files.Seed(avatar);
        var bus = new FakeMessageBus();

        await Run(AvatarMessage(Guid.NewGuid(), AvatarTarget(avatar.Id, "User", ownerId)), files, bus);

        Assert.Equal(FileStatus.Available, avatar.Status);
        Assert.Equal("not_found", Assert.Single(bus.Published.OfType<FileDeletionSkippedIntegrationEvent>()).Reason);
    }

    private static FileDeletionTarget AvatarTarget(Guid fileId, string ownerType, Guid ownerId) =>
        new()
        {
            FileId = fileId,
            ExpectedOwnerType = ownerType,
            ExpectedOwnerId = ownerId,
            ExpectedFolderType = "Avatars",
        };

    private static FilesDeletionRequestedIntegrationEvent AvatarMessage(
        Guid tenantId,
        params FileDeletionTarget[] files
    ) =>
        new()
        {
            TenantId = tenantId,
            SourceService = "auth.avatar",
            SourceReferenceId = Guid.NewGuid(),
            Reason = "avatar_replaced",
            Files = files,
            CorrelationId = "corr-avatar",
        };

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-1";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
