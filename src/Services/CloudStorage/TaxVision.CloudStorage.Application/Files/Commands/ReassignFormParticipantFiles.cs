using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.Commands;

public sealed record ReassignFormParticipantFilesRequest(
    Guid TenantId,
    Guid FormRequestId,
    Guid ParticipantId,
    Guid CustomerId,
    IReadOnlyList<Guid> FileIds
);

public enum FormParticipantFileReassignmentStatus
{
    Reassigned,
    AlreadyApplied,
    NotFound,
    OwnerMismatch,
    LegalHold,
}

public sealed record FormParticipantFileReassignmentResult(Guid FileId, FormParticipantFileReassignmentStatus Status);

public sealed record ReassignFormParticipantFilesResponse(IReadOnlyList<FormParticipantFileReassignmentResult> Files);

public sealed record ReassignFormParticipantFilesCommand(
    Guid ActorId,
    ReassignFormParticipantFilesRequest Request,
    RequestAuditContext Audit
);

public static class ReassignFormParticipantFilesHandler
{
    public static async Task<Result<ReassignFormParticipantFilesResponse>> Handle(
        ReassignFormParticipantFilesCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemFolderProvisioner systemFolders,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = command.Request;
        if (
            request.TenantId == Guid.Empty
            || request.FormRequestId == Guid.Empty
            || request.ParticipantId == Guid.Empty
            || request.CustomerId == Guid.Empty
            || request.FileIds is not { Count: > 0 and <= 50 }
            || request.FileIds.Any(id => id == Guid.Empty)
            || request.FileIds.Distinct().Count() != request.FileIds.Count
        )
        {
            return Result.Failure<ReassignFormParticipantFilesResponse>(FileErrors.InvalidReassignmentRequest);
        }

        var results = new List<FormParticipantFileReassignmentResult>(request.FileIds.Count);
        Guid? destinationFolderId = null;
        var changed = false;

        foreach (var fileId in request.FileIds)
        {
            // Lectura bloqueada: el estado comprobado abajo no puede cambiar antes del commit.
            var file = await files.GetForUpdateAsync(request.TenantId, fileId, ct);
            if (file is null)
            {
                results.Add(new(fileId, FormParticipantFileReassignmentStatus.NotFound));
                continue;
            }

            if (
                file.FolderType == FolderType.ClientForms
                && file.OwnerType == OwnerType.Customer
                && file.OwnerId == request.CustomerId
            )
            {
                results.Add(new(fileId, FormParticipantFileReassignmentStatus.AlreadyApplied));
                continue;
            }

            if (
                file.FolderType != FolderType.ClientForms
                || file.OwnerType != OwnerType.FormParticipant
                || file.OwnerId != request.ParticipantId
                || file.Status is FileStatus.SoftDeleted or FileStatus.Infected
            )
            {
                results.Add(new(fileId, FormParticipantFileReassignmentStatus.OwnerMismatch));
                continue;
            }

            if (file.IsLegalHeld)
            {
                results.Add(new(fileId, FormParticipantFileReassignmentStatus.LegalHold));
                continue;
            }

            destinationFolderId ??= await systemFolders.ResolveFolderIdAsync(
                request.TenantId,
                OwnerType.Customer,
                request.CustomerId,
                FolderType.ClientForms,
                command.ActorId,
                clock.UtcNow,
                ct
            );

            var reassigned = file.ReassignOwner(OwnerType.Customer, request.CustomerId, clock.UtcNow);
            if (reassigned.IsFailure)
            {
                results.Add(new(fileId, FormParticipantFileReassignmentStatus.OwnerMismatch));
                continue;
            }

            file.MoveToFolder(destinationFolderId, clock.UtcNow);
            audit.Add(
                StorageAccessLog.Create(
                    request.TenantId,
                    file.Id,
                    command.ActorId,
                    "file.owner_reassigned_from_form_participant",
                    "success",
                    command.Audit.IpAddress,
                    command.Audit.UserAgent,
                    command.Audit.CorrelationId,
                    $"formRequestId={request.FormRequestId:D};participantId={request.ParticipantId:D};customerId={request.CustomerId:D}",
                    clock.UtcNow
                )
            );
            results.Add(new(fileId, FormParticipantFileReassignmentStatus.Reassigned));
            changed = true;
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ReassignFormParticipantFilesResponse(results));
    }
}
