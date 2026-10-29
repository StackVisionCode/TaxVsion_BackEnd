using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class ArchiveConsentNoticeHandler
{
    public static async Task<Result> Handle(
        ArchiveConsentNoticeCommand command,
        IConsentNoticeRepository notices,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(command.TenantId, command.NoticeId, ct);
        if (notice is null)
            return Result.Failure(ConsentErrors.NoticeNotFound);
        if (!notice.RowVersion.AsSpan().SequenceEqual(command.RowVersion))
            return Result.Failure(ConsentErrors.ConcurrencyConflict);
        var archived = notice.Archive(command.ByUserId, command.NowUtc);
        if (archived.IsFailure)
            return archived;
        audit.Record(command.TenantId, null, null, FormAuditAction.ConsentNoticeArchived, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
