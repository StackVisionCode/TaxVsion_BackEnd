using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class EntryNoticeEvidenceRecorder
{
    public const string ComponentId = "entry_notice";

    public static async Task<Result> RecordAsync(
        ParticipantContext context,
        FormAccessChallenge challenge,
        string clientIp,
        string userAgent,
        DateTime nowUtc,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository records,
        IFormAuditLog audit,
        CancellationToken ct
    )
    {
        var selection = await EntryNoticeSupport.ResolvePinnedAsync(
            context.TenantId,
            challenge.EntryNoticeId,
            challenge.EntryNoticeVersionNumber,
            notices,
            ct
        );
        // El código guarda versión y hash; el texto mostrado se toma de la versión fijada.
        if (selection is null || selection.Version.ContentHash != challenge.EntryNoticeContentHash)
            return Result.Failure(ConsentErrors.VersionNotFound);
        var existing = await records.GetAsync(
            FormConsentRecordKey.For(
                context,
                context.BoundRequestId!.Value,
                ComponentId,
                selection.Version,
                FormConsentOutcome.Presented
            ),
            ct
        );
        if (existing is not null)
            return Result.Success();
        var created = FormConsentRecord.Create(
            context.TenantId,
            context.BoundRequestId.Value,
            ComponentId,
            context.Kind,
            context.CustomerId,
            context.ParticipantId,
            context.Actor,
            selection.Version,
            null,
            FormConsentOutcome.Presented,
            clientIp,
            userAgent,
            nowUtc
        );
        if (created.IsFailure)
            return Result.Failure(created.Error);
        records.Add(created.Value);
        audit.RecordParticipant(
            context.TenantId,
            context.BoundRequestId.Value,
            FormAuditAction.ConsentPresented,
            context.Actor
        );
        return Result.Success();
    }
}
