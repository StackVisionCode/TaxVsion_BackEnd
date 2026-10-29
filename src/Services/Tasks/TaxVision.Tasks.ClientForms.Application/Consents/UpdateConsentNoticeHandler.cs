using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class UpdateConsentNoticeHandler
{
    public static async Task<Result<ConsentNoticeResponse>> Handle(
        UpdateConsentNoticeCommand command,
        IConsentNoticeRepository notices,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(command.TenantId, command.NoticeId, ct);
        if (notice is null)
            return Result.Failure<ConsentNoticeResponse>(ConsentErrors.NoticeNotFound);
        if (!notice.RowVersion.AsSpan().SequenceEqual(command.RowVersion))
            return Result.Failure<ConsentNoticeResponse>(ConsentErrors.ConcurrencyConflict);
        var updated = notice.UpdateTitle(command.Title);
        if (updated.IsFailure)
            return Result.Failure<ConsentNoticeResponse>(updated.Error);
        audit.Record(command.TenantId, null, null, FormAuditAction.ConsentNoticeUpdated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ConsentNoticeResponse.From(notice));
    }
}
