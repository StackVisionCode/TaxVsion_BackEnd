using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class ReassignFormParticipantFilesHandlerTests
{
    [Fact]
    public async Task Reassigns_only_matching_client_forms_files_and_audits_without_filename()
    {
        var tenantId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var formRequestId = Guid.NewGuid();
        var file = ParticipantFile(tenantId, participantId, FolderType.ClientForms, "private-name.pdf");
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await Handle(
            tenantId,
            formRequestId,
            participantId,
            customerId,
            [file.Id],
            files,
            folders,
            audit,
            unitOfWork
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormParticipantFileReassignmentStatus.Reassigned, result.Value.Files.Single().Status);
        Assert.Equal(OwnerType.Customer, file.OwnerType);
        Assert.Equal(customerId, file.OwnerId);
        Assert.NotNull(file.FolderId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, files.LockedReads);
        var log = Assert.Single(audit.Logs);
        Assert.Equal("file.owner_reassigned_from_form_participant", log.Action);
        Assert.DoesNotContain("private-name.pdf", log.Details);
    }

    [Fact]
    public async Task Repeated_request_is_already_applied_without_another_write()
    {
        var tenantId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = ParticipantFile(tenantId, participantId, FolderType.ClientForms, "form.pdf");
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();

        await Handle(
            tenantId,
            Guid.NewGuid(),
            participantId,
            customerId,
            [file.Id],
            files,
            folders,
            new FakeStorageAuditRepository(),
            new FakeUnitOfWork()
        );
        var secondUnitOfWork = new FakeUnitOfWork();

        var second = await Handle(
            tenantId,
            Guid.NewGuid(),
            participantId,
            customerId,
            [file.Id],
            files,
            folders,
            new FakeStorageAuditRepository(),
            secondUnitOfWork
        );

        Assert.Equal(FormParticipantFileReassignmentStatus.AlreadyApplied, second.Value.Files.Single().Status);
        Assert.Equal(0, secondUnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_wrong_owner_other_tenant_non_client_forms_and_legal_hold()
    {
        var tenantId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var wrongOwner = ParticipantFile(tenantId, Guid.NewGuid(), FolderType.ClientForms, "a.pdf");
        var otherTenant = ParticipantFile(Guid.NewGuid(), participantId, FolderType.ClientForms, "b.pdf");
        var wrongFolder = ParticipantFile(tenantId, participantId, FolderType.Documents, "c.pdf");
        var held = ParticipantFile(tenantId, participantId, FolderType.ClientForms, "d.pdf");
        held.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(wrongOwner);
        files.Seed(otherTenant);
        files.Seed(wrongFolder);
        files.Seed(held);
        var unitOfWork = new FakeUnitOfWork();

        var result = await Handle(
            tenantId,
            Guid.NewGuid(),
            participantId,
            Guid.NewGuid(),
            [wrongOwner.Id, otherTenant.Id, wrongFolder.Id, held.Id],
            files,
            new FakeFolderRepository(),
            new FakeStorageAuditRepository(),
            unitOfWork
        );

        Assert.Equal(
            [
                FormParticipantFileReassignmentStatus.OwnerMismatch,
                FormParticipantFileReassignmentStatus.NotFound,
                FormParticipantFileReassignmentStatus.OwnerMismatch,
                FormParticipantFileReassignmentStatus.LegalHold,
            ],
            result.Value.Files.Select(item => item.Status)
        );
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_more_than_fifty_files()
    {
        var result = await Handle(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray(),
            new FakeFileObjectRepository(),
            new FakeFolderRepository(),
            new FakeStorageAuditRepository(),
            new FakeUnitOfWork()
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidReassignmentRequest, result.Error);
    }

    private static Task<BuildingBlocks.Results.Result<ReassignFormParticipantFilesResponse>> Handle(
        Guid tenantId,
        Guid formRequestId,
        Guid participantId,
        Guid customerId,
        IReadOnlyList<Guid> fileIds,
        FakeFileObjectRepository files,
        FakeFolderRepository folders,
        FakeStorageAuditRepository audit,
        FakeUnitOfWork unitOfWork
    ) =>
        ReassignFormParticipantFilesHandler.Handle(
            new ReassignFormParticipantFilesCommand(
                Guid.NewGuid(),
                new ReassignFormParticipantFilesRequest(tenantId, formRequestId, participantId, customerId, fileIds),
                new RequestAuditContext("127.0.0.1", "test", "corr-1")
            ),
            files,
            audit,
            new SystemFolderProvisioner(folders),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

    private static FileObject ParticipantFile(Guid tenantId, Guid participantId, FolderType folderType, string name) =>
        FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.FormParticipant,
                participantId,
                folderType,
                folderType.RequiresYear() ? 2026 : null,
                ObjectKey
                    .Create(
                        $"tenants/{tenantId:N}/form-participant/{participantId:N}/{folderType}/{Guid.NewGuid():N}.pdf"
                    )
                    .Value,
                name,
                "application/pdf",
                1024,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1)
            )
            .Value;
}
