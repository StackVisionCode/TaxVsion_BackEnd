using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public static class AcceptFormConsentHandler
{
    public static async Task<Result<AcceptFormConsentResponse>> Handle(
        AcceptFormConsentCommand command,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository records,
        IAnswersProtector protector,
        IFormEngine engine,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var resolved = await FormConsentComponentResolver.ResolveAsync(
            command.Context,
            command.RequestId,
            command.ComponentId,
            new FormConsentRuntime(requests, versions, notices, protector, engine),
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<AcceptFormConsentResponse>(resolved.Error);
        var outcome = OutcomeOf(resolved.Value);
        var recorded = await FormConsentEvidenceWriter.RecordAsync(
            Key(command, resolved.Value, outcome),
            () => Evidence(command, resolved.Value, outcome),
            records,
            unitOfWork,
            ct
        );
        if (recorded.IsFailure)
            return Result.Failure<AcceptFormConsentResponse>(recorded.Error);
        if (recorded.Value.Created)
            await AuditAsync(command, outcome, audit, unitOfWork, metrics, ct);
        var record = recorded.Value.Record;
        return Result.Success(new AcceptFormConsentResponse(record.Id, record.RecordedAtUtc));
    }

    private static FormConsentOutcome OutcomeOf(ResolvedFormConsent resolved) =>
        resolved.Consent.Mode == "acceptance" ? FormConsentOutcome.Accepted : FormConsentOutcome.Presented;

    private static FormConsentRecordKey Key(
        AcceptFormConsentCommand command,
        ResolvedFormConsent resolved,
        FormConsentOutcome outcome
    ) =>
        FormConsentRecordKey.For(
            command.Context,
            command.RequestId,
            command.ComponentId,
            resolved.NoticeVersion,
            outcome
        );

    private static Result<FormConsentRecord> Evidence(
        AcceptFormConsentCommand command,
        ResolvedFormConsent resolved,
        FormConsentOutcome outcome
    ) =>
        FormConsentRecord.Create(
            command.Context.TenantId,
            command.RequestId,
            command.ComponentId,
            command.Context.Kind,
            command.Context.CustomerId,
            command.Context.ParticipantId,
            command.Context.Actor,
            resolved.NoticeVersion,
            outcome == FormConsentOutcome.Accepted ? resolved.Consent.AcceptanceText : null,
            outcome,
            command.ClientIp,
            command.UserAgent,
            command.NowUtc
        );

    private static async Task AuditAsync(
        AcceptFormConsentCommand command,
        FormConsentOutcome outcome,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        audit.RecordParticipant(
            command.Context.TenantId,
            command.RequestId,
            outcome == FormConsentOutcome.Accepted ? FormAuditAction.ConsentAccepted : FormAuditAction.ConsentPresented,
            command.Context.Actor
        );
        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordConsent(outcome);
    }
}
