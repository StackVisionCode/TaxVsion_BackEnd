using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class ListConsentNoticesHandler
{
    public static async Task<IReadOnlyList<ConsentNoticeResponse>> Handle(
        ListConsentNoticesQuery query,
        IConsentNoticeRepository notices,
        CancellationToken ct
    ) => (await notices.ListAsync(query.TenantId, ct)).Select(ConsentNoticeResponse.From).ToArray();
}
