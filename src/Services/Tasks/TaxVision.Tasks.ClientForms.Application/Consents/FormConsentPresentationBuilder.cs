using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class FormConsentPresentationBuilder
{
    public static async Task<IReadOnlyList<FormConsentPresentationResponse>> BuildAsync(
        ParticipantContext context,
        Guid requestId,
        FormDefinition definition,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository records,
        CancellationToken ct
    )
    {
        var evidence = await records.ListForRequestAsync(context.TenantId, requestId, ct);
        var result = new List<FormConsentPresentationResponse>();
        foreach (var consent in FormConsentDefinitionSupport.Read(definition))
        {
            var notice = await notices.GetByKeyAsync(context.TenantId, consent.NoticeKey, ct);
            var version = notice?.Versions.SingleOrDefault(item => item.VersionNumber == consent.NoticeVersionNumber);
            if (notice is null || version is null)
                continue;
            var record = evidence.LastOrDefault(item =>
                item.ComponentId == consent.ComponentId
                && item.ConsentNoticeId == notice.Id
                && item.NoticeVersionNumber == consent.NoticeVersionNumber
                && FormConsentGate.MatchesParticipant(context, item)
            );
            result.Add(new FormConsentPresentationResponse(
                consent.ComponentId,
                consent.NoticeKey,
                consent.NoticeVersionNumber,
                consent.Mode,
                consent.Timing,
                consent.AcceptanceText,
                consent.Blocking,
                version.Language,
                JsonDocument.Parse(version.BodyJson).RootElement.Clone(),
                version.ContentHash,
                record is null ? null : FormConsentEvidenceResponse.From(record)
            ));
        }
        return result;
    }
}
