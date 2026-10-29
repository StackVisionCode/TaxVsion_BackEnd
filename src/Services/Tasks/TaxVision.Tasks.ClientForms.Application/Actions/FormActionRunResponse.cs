using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

// Estado real de cada operación asíncrona: Requested no es éxito hasta que llega su resultado.
public sealed record FormActionRunResponse(
    Guid Id,
    int RevisionNumber,
    string ActionKey,
    string ActionType,
    FormActionRunStatus Status,
    int Attempt,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    string? ResultRefKind,
    Guid? ResultRefId,
    Guid? ExistingCustomerId,
    string? ErrorCode,
    string? ErrorDetail,
    IReadOnlyList<string> Warnings,
    bool IsLatestAttempt,
    bool CanRetry
)
{
    public static FormActionRunResponse From(FormActionRun run, bool showExistingCustomer = true) =>
        new(
            run.Id,
            run.RevisionNumber,
            run.ActionKey,
            run.ActionType,
            run.Status,
            run.Attempt,
            run.RequestedAtUtc,
            run.CompletedAtUtc,
            run.ResultRefKind,
            run.ResultRefId,
            showExistingCustomer ? run.ExistingCustomerId : null,
            run.ErrorCode,
            run.ErrorDetail,
            string.IsNullOrWhiteSpace(run.Warnings) ? [] : run.Warnings.Split(','),
            run.IsLatestAttempt,
            run.IsRetryable
        );
}
