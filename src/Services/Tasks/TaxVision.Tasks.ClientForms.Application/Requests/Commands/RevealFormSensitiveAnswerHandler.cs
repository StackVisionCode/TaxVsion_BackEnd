using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record RevealFormSensitiveAnswerCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    Guid SensitiveAnswerId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId
);

public sealed record RevealedFormSensitiveAnswerResponse(string Value, FormTaxIdentifierKind Kind);

public static class RevealFormSensitiveAnswerHandler
{
    private const string Action = "sensitive.revealed";

    public static async Task<Result<RevealedFormSensitiveAnswerResponse>> Handle(
        RevealFormSensitiveAnswerCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        ISensitiveValueProtector protector,
        IAnswersProtector answersProtector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(command.TenantId, command.RequestId, ct);
        var visible =
            found.IsSuccess
            && await visibility.CanViewAsync(command.TenantId, command.ByUserId, command.CanViewAll, found.Value, ct);
        if (!visible)
            return await DeniedAsync(command, null, audit, unitOfWork, ct);
        var answer = FindSubmittedAnswer(found.Value, command.SensitiveAnswerId, answersProtector);
        if (answer is null)
            return await DeniedAsync(command, found.Value, audit, unitOfWork, ct);

        var raw = protector.Unprotect(new(answer.Protected.Cipher, answer.Protected.KeyVersion));
        RecordGranted(command, found.Value, answer, audit);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new RevealedFormSensitiveAnswerResponse(Format(raw, answer.Kind), answer.Kind));
    }

    // El staff no ve el borrador: solo se revela lo que alguna revisión enviada referencia.
    private static FormSensitiveAnswer? FindSubmittedAnswer(
        FormRequest request,
        Guid answerId,
        IAnswersProtector answersProtector
    )
    {
        var answer = request.Submission?.SensitiveAnswers.FirstOrDefault(item => item.Id == answerId);
        if (answer is null)
            return null;
        var submitted = new HashSet<Guid>();
        foreach (var revision in request.Submission!.Revisions)
            FormSensitiveReferenceReader.Collect(answersProtector.Unprotect(revision.Answers), submitted);
        return submitted.Contains(answerId) ? answer : null;
    }

    private static void RecordGranted(
        RevealFormSensitiveAnswerCommand command,
        FormRequest request,
        FormSensitiveAnswer answer,
        IFormAuditLog audit
    ) =>
        audit.RecordStaffOutcome(
            command.TenantId,
            request.FormTemplateId,
            command.RequestId,
            Action,
            "granted",
            command.ByUserId,
            $"{{\"sensitiveAnswerId\":\"{answer.Id:D}\",\"kind\":\"{answer.Kind}\"}}",
            RequestContext(command)
        );

    // Sin request visible se audita como inexistente; con request, como respuesta sensible no encontrada.
    private static async Task<Result<RevealedFormSensitiveAnswerResponse>> DeniedAsync(
        RevealFormSensitiveAnswerCommand command,
        FormRequest? visibleRequest,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        audit.RecordStaffOutcome(
            command.TenantId,
            visibleRequest?.FormTemplateId,
            command.RequestId,
            Action,
            visibleRequest is null ? "denied.not_found" : "denied.no_sensitive_answer",
            command.ByUserId,
            $"{{\"sensitiveAnswerId\":\"{command.SensitiveAnswerId:D}\"}}",
            RequestContext(command)
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<RevealedFormSensitiveAnswerResponse>(
            visibleRequest is null ? FormRequestErrors.NotFound : FormRequestErrors.SensitiveAnswerNotFound
        );
    }

    private static FormAuditRequestContext RequestContext(RevealFormSensitiveAnswerCommand command) =>
        new(command.IpAddress, command.UserAgent, command.CorrelationId);

    private static string Format(string value, FormTaxIdentifierKind kind) =>
        kind switch
        {
            FormTaxIdentifierKind.Ein when value.Length == 9 => $"{value[..2]}-{value[2..]}",
            FormTaxIdentifierKind.Ssn or FormTaxIdentifierKind.Itin when value.Length == 9 =>
                $"{value[..3]}-{value[3..5]}-{value[5..]}",
            _ => value,
        };
}
