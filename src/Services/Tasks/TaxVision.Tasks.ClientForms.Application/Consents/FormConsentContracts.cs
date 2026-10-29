using System.Text.Json;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public sealed record AcceptFormConsentCommand(
    ParticipantContext Context,
    Guid RequestId,
    string ComponentId,
    string ClientIp,
    string UserAgent,
    DateTime NowUtc
);

public sealed record AcceptFormConsentResponse(Guid RecordId, DateTime AcceptedAtUtc);

public sealed record DeclineFormConsentCommand(
    ParticipantContext Context,
    Guid RequestId,
    string ComponentId,
    string ClientIp,
    string UserAgent,
    DateTime NowUtc
);

public sealed record DeclineFormConsentResponse(Guid RecordId, DateTime DeclinedAtUtc);

public sealed record FormConsentEvidenceResponse(
    Guid RecordId,
    int? RevisionNumber,
    FormConsentOutcome Outcome,
    int NoticeVersionNumber,
    string NoticeContentHash,
    string TextSnapshot,
    string Language,
    string? AcceptanceText,
    DateTime RecordedAtUtc,
    string ClientIp,
    string UserAgent
)
{
    internal static FormConsentEvidenceResponse From(FormConsentRecord record) =>
        new(
            record.Id,
            record.RevisionNumber,
            record.Outcome,
            record.NoticeVersionNumber,
            record.NoticeContentHash,
            record.TextSnapshot,
            record.Language,
            record.AcceptanceText,
            record.RecordedAtUtc,
            record.ClientIp,
            record.UserAgent
        );
}

public sealed record FormConsentPresentationResponse(
    string ComponentId,
    string NoticeKey,
    int NoticeVersionNumber,
    string Mode,
    string? Timing,
    string? AcceptanceText,
    bool Blocking,
    string Language,
    JsonElement Body,
    string ContentHash,
    FormConsentEvidenceResponse? Evidence
);
