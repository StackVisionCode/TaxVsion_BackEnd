using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

internal static class PublicFormSessionOpener
{
    // Abre la sesión del participante y deja la evidencia del aviso de entrada; null si algo no cuadra.
    internal static async Task<(string RawToken, FormAccessSession Session)?> OpenAsync(
        Guid tenantId,
        Guid requestId,
        Guid participantId,
        FormAccessChallenge challenge,
        string clientIp,
        string userAgent,
        DateTime nowUtc,
        IFormAccessSessionRepository sessions,
        IFormExternalAccessSecurity security,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository consentRecords,
        IFormAuditLog audit,
        CancellationToken ct
    )
    {
        var token = security.GenerateToken();
        var session = FormAccessSession.Create(
            tenantId,
            requestId,
            participantId,
            token.TokenHash,
            clientIp,
            userAgent,
            nowUtc
        );
        if (session.IsFailure)
            return null;
        sessions.Add(session.Value);
        var context = ParticipantContext.External(tenantId, participantId, requestId, session.Value.Id);
        var evidence = await EntryNoticeEvidenceRecorder.RecordAsync(
            context,
            challenge,
            clientIp,
            userAgent,
            nowUtc,
            notices,
            consentRecords,
            audit,
            ct
        );
        return evidence.IsFailure ? null : (token.RawToken, session.Value);
    }
}
