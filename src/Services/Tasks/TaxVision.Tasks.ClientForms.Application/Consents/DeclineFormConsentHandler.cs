using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

// El rechazo explícito queda como evidencia; retirar un consentimiento ya aceptado lo gestiona la oficina.
public static class DeclineFormConsentHandler
{
    public static async Task<Result<DeclineFormConsentResponse>> Handle(
        DeclineFormConsentCommand command,
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
        var resolved = await ResolveDeclinableAsync(
            command,
            new FormConsentRuntime(requests, versions, notices, protector, engine),
            records,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<DeclineFormConsentResponse>(resolved.Error);
        var recorded = await FormConsentEvidenceWriter.RecordAsync(
            Key(command, resolved.Value, FormConsentOutcome.Declined),
            () => Evidence(command, resolved.Value),
            records,
            unitOfWork,
            ct
        );
        if (recorded.IsFailure)
            return Result.Failure<DeclineFormConsentResponse>(recorded.Error);
        if (recorded.Value.Created)
            await AuditAsync(command, audit, unitOfWork, metrics, ct);
        var record = recorded.Value.Record;
        return Result.Success(new DeclineFormConsentResponse(record.Id, record.RecordedAtUtc));
    }

    private static async Task<Result<ResolvedFormConsent>> ResolveDeclinableAsync(
        DeclineFormConsentCommand command,
        FormConsentRuntime runtime,
        IFormConsentRecordRepository records,
        CancellationToken ct
    )
    {
        var resolved = await FormConsentComponentResolver.ResolveAsync(
            command.Context,
            command.RequestId,
            command.ComponentId,
            runtime,
            ct
        );
        if (resolved.IsFailure)
            return resolved;
        if (resolved.Value.Consent.Mode != "acceptance")
            return Result.Failure<ResolvedFormConsent>(FormRequestErrors.InvalidComponent);
        var accepted = await records.GetAsync(Key(command, resolved.Value, FormConsentOutcome.Accepted), ct);
        return accepted is null ? resolved : Result.Failure<ResolvedFormConsent>(ConsentErrors.AlreadyAccepted);
    }

    private static FormConsentRecordKey Key(
        DeclineFormConsentCommand command,
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
        DeclineFormConsentCommand command,
        ResolvedFormConsent resolved
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
            null,
            FormConsentOutcome.Declined,
            command.ClientIp,
            command.UserAgent,
            command.NowUtc
        );

    private static async Task AuditAsync(
        DeclineFormConsentCommand command,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        audit.RecordParticipant(
            command.Context.TenantId,
            command.RequestId,
            FormAuditAction.ConsentDeclined,
            command.Context.Actor
        );
        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordConsent(FormConsentOutcome.Declined);
    }
}
