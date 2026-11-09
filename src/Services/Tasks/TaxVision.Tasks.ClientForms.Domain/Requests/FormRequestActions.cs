using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// Las acciones del formulario (crear cliente, pedir firma…): intentos, reintentos y resultado. Son parte del agregado
/// <see cref="FormRequest"/>: se guardan con él y comparten su RowVersion (TSK-9).
/// </summary>
public sealed class FormRequestActions
{
    private readonly FormRequest _request;
    private readonly List<FormActionRun> _runs;

    internal FormRequestActions(FormRequest request, List<FormActionRun> runs)
    {
        _request = request;
        _runs = runs;
    }

    public Result<FormActionRun> Request(string actionKey, string actionType, Guid commandId, DateTime nowUtc) =>
        commandId == Guid.Empty
            ? Result.Failure<FormActionRun>(FormActionRunErrors.Invalid)
            : AddRun(actionKey, actionType, null, commandId, nowUtc);

    // Termina sin emitir nada: Skipped (condición o disponibilidad) o Failed (no se pudo armar el comando).
    public Result<FormActionRun> CloseWithoutCommand(
        string actionKey,
        string actionType,
        FormActionRunStatus status,
        string errorCode,
        DateTime nowUtc
    )
    {
        if (status is not (FormActionRunStatus.Skipped or FormActionRunStatus.Failed))
            return Result.Failure<FormActionRun>(FormActionRunErrors.Invalid);
        if (string.IsNullOrWhiteSpace(errorCode))
            return Result.Failure<FormActionRun>(FormActionRunErrors.Invalid);
        var run = AddRun(actionKey, actionType, null, null, nowUtc);
        if (run.IsSuccess)
            run.Value.CloseWithoutCommand(status, errorCode, nowUtc);
        return run;
    }

    public Result<FormActionRun> GetRetryable(Guid runId)
    {
        var run = _runs.Find(item => item.Id == runId);
        if (run is null)
            return Result.Failure<FormActionRun>(FormActionRunErrors.NotFound);
        return run.IsRetryable ? Result.Success(run) : Result.Failure<FormActionRun>(FormActionRunErrors.NotRetryable);
    }

    // Mismo IdempotencyKey, Attempt + 1. Sin comando (failureCode), el intento nace fallido.
    public Result<FormActionRun> Retry(Guid runId, Guid? commandId, string? failureCode, DateTime nowUtc)
    {
        var previous = GetRetryable(runId);
        if (previous.IsFailure)
            return previous;
        if ((commandId is null) == string.IsNullOrWhiteSpace(failureCode) || commandId == Guid.Empty)
            return Result.Failure<FormActionRun>(FormActionRunErrors.Invalid);
        var next = AddRun(
            previous.Value.ActionKey,
            previous.Value.ActionType,
            previous.Value.IdempotencyKey,
            commandId,
            nowUtc
        );
        if (next.IsFailure)
            return next;
        previous.Value.MarkRetried(nowUtc);
        if (failureCode is not null)
            next.Value.CloseWithoutCommand(FormActionRunStatus.Failed, failureCode, nowUtc);
        return next;
    }

    public bool HasActive(string actionType) =>
        _runs.Exists(run => run.ActionType == actionType && run.Status != FormActionRunStatus.Skipped);

    // Ya hay un cierre sin comando de esta acción en la revisión con ese motivo (eventos repetidos).
    public bool HasClosed(string actionKey, int revisionNumber, string errorCode) =>
        _runs.Exists(run =>
            run.ActionKey == actionKey
            && run.RevisionNumber == revisionNumber
            && run.Status == FormActionRunStatus.Skipped
            && run.ErrorCode == errorCode
        );

    public FormActionRun? Latest(string actionType) =>
        _runs.Find(run =>
            run.ActionType == actionType && run.IsLatestAttempt && run.Status != FormActionRunStatus.Skipped
        );

    public FormActionRun? FindByCommand(Guid commandId) =>
        commandId == Guid.Empty ? null : _runs.Find(run => run.CommandId == commandId);

    public Result<FormActionRunTransition> Complete(
        Guid commandId,
        string resultKind,
        Guid resultId,
        string? warnings,
        DateTime nowUtc
    )
    {
        var run = FindByCommand(commandId);
        if (run is null)
            return Result.Failure<FormActionRunTransition>(FormActionRunErrors.NotFound);
        if (resultId == Guid.Empty || string.IsNullOrWhiteSpace(resultKind))
            return Result.Failure<FormActionRunTransition>(FormActionRunErrors.Invalid);
        return Result.Success(run.Complete(resultKind, resultId, warnings, nowUtc));
    }

    public Result<FormActionRunTransition> Reject(
        Guid commandId,
        string errorCode,
        Guid? existingCustomerId,
        string? detail,
        DateTime nowUtc
    )
    {
        var run = FindByCommand(commandId);
        if (run is null)
            return Result.Failure<FormActionRunTransition>(FormActionRunErrors.NotFound);
        if (string.IsNullOrWhiteSpace(errorCode))
            return Result.Failure<FormActionRunTransition>(FormActionRunErrors.Invalid);
        return Result.Success(run.Reject(errorCode, existingCustomerId, detail, nowUtc));
    }

    private Result<FormActionRun> AddRun(
        string actionKey,
        string actionType,
        string? idempotencyKey,
        Guid? commandId,
        DateTime nowUtc
    )
    {
        if (
            _request.CurrentRevisionNumber < 1
            || string.IsNullOrWhiteSpace(actionKey)
            || actionKey.Length > FormActionRun.ActionKeyMaxLength
            || string.IsNullOrWhiteSpace(actionType)
            || actionType.Length > FormActionRun.ActionTypeMaxLength
        )
            return Result.Failure<FormActionRun>(FormActionRunErrors.Invalid);
        var key =
            idempotencyKey ?? FormActionRun.IdempotencyKeyFor(_request.Id, _request.CurrentRevisionNumber, actionKey);
        var attempt = 1;
        foreach (var existing in _runs)
        {
            if (existing.IdempotencyKey == key && existing.Attempt >= attempt)
                attempt = existing.Attempt + 1;
        }
        var run = FormActionRun.Create(
            _request.TenantId,
            _request.Id,
            _request.CurrentRevisionNumber,
            actionKey,
            actionType,
            key,
            attempt,
            commandId,
            nowUtc
        );
        _runs.Add(run);
        return Result.Success(run);
    }
}
