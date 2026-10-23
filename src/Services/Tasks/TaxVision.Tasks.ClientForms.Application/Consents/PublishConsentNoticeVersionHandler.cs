using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Text;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class PublishConsentNoticeVersionHandler
{
    public static async Task<Result<ConsentNoticeResponse>> Handle(
        PublishConsentNoticeVersionCommand command,
        IConsentNoticeRepository notices,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(command.TenantId, command.NoticeId, ct);
        if (notice is null)
            return Failure(ConsentErrors.NoticeNotFound);
        if (!notice.RowVersion.AsSpan().SequenceEqual(command.RowVersion))
            return Failure(ConsentErrors.ConcurrencyConflict);
        if (RichTextCanonicalizer.Validate(command.Body, 20_000).Count > 0)
            return Failure(ConsentErrors.NoticeInvalid);
        var bodyJson = RichTextCanonicalizer.Canonicalize(command.Body);
        var text = RichTextCanonicalizer.PlainText(command.Body).Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyJson))).ToLowerInvariant();
        var published = notice.Publish(
            command.Language,
            bodyJson,
            text,
            hash,
            command.ByUserId,
            command.NowUtc
        );
        if (published.IsFailure)
            return Failure(published.Error);
        audit.Record(command.TenantId, null, null, FormAuditAction.ConsentNoticePublished, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ConsentNoticeResponse.From(notice));
    }

    private static Result<ConsentNoticeResponse> Failure(Error error) =>
        Result.Failure<ConsentNoticeResponse>(error);
}
