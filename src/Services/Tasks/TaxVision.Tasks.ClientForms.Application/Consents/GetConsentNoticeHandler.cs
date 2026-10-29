using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class GetConsentNoticeHandler
{
    public static async Task<Result<ConsentNoticeResponse>> Handle(
        GetConsentNoticeQuery query,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var notice = await notices.GetAsync(query.TenantId, query.NoticeId, ct);
        return notice is null
            ? Result.Failure<ConsentNoticeResponse>(ConsentErrors.NoticeNotFound)
            : Result.Success(ConsentNoticeResponse.From(notice));
    }
}
