using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class CreateConsentNoticeHandler
{
    public static async Task<Result<ConsentNoticeResponse>> Handle(
        CreateConsentNoticeCommand command,
        IConsentNoticeRepository notices,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (await notices.GetByKeyAsync(command.TenantId, command.Key, ct) is not null)
            return Result.Failure<ConsentNoticeResponse>(ConsentErrors.NoticeKeyTaken);
        var created = ConsentNotice.Create(
            command.TenantId,
            command.Key,
            command.Title,
            command.Purpose,
            command.ByUserId,
            command.NowUtc
        );
        if (created.IsFailure)
            return Result.Failure<ConsentNoticeResponse>(created.Error);
        notices.Add(created.Value);
        audit.Record(command.TenantId, null, null, FormAuditAction.ConsentNoticeCreated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ConsentNoticeResponse.From(created.Value));
    }
}
