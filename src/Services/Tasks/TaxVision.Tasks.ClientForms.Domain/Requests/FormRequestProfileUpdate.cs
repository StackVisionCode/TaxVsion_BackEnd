using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// P-13 — aplicar las respuestas aprobadas al perfil del cliente. Parte del agregado <see cref="FormRequest"/>: pasa a
/// Processing y queda a la espera del hecho de Customer. La autorización ya la validó el handler antes de llamar aquí.
/// </summary>
public sealed class FormRequestProfileUpdate
{
    private readonly FormRequest _request;
    private readonly List<FormProfileUpdateOutcome> _outcomes;

    internal FormRequestProfileUpdate(FormRequest request, List<FormProfileUpdateOutcome> outcomes)
    {
        _request = request;
        _outcomes = outcomes;
    }

    public Result Request(Guid commandId, IReadOnlyCollection<string> targets, DateTime nowUtc)
    {
        if (_request.Status != FormRequestStatus.Completed || !_request.CustomerId.HasValue)
            return Result.Failure(FormRequestErrors.ProfileUpdateNotAllowed);
        if (_request.ProfileUpdateStatus == FormProfileUpdateStatus.Processing)
            return Result.Failure(FormRequestErrors.ProfileUpdateInProgress);
        if (commandId == Guid.Empty || targets.Count == 0)
            return Result.Failure(FormRequestErrors.ProfileUpdateNoTargets);

        _request.ProfileUpdateStatus = FormProfileUpdateStatus.Processing;
        _request.ProfileUpdateCommandId = commandId;
        _request.ProfileUpdateRequestedAtUtc = nowUtc;
        _request.ProfileUpdateResolvedAtUtc = null;
        _outcomes.Clear();
        foreach (var target in targets)
            _outcomes.Add(new FormProfileUpdateOutcome(target, false, null));
        _request.Touch(nowUtc);
        return Result.Success();
    }

    // Hecho Applied: aplicados + fallidos. Con fallos => PartiallyApplied. Idempotente si ya se resolvió.
    public Result MarkApplied(Guid commandId, IReadOnlyCollection<FormProfileUpdateOutcome> outcomes, DateTime nowUtc)
    {
        var guard = EnsureCommand(commandId);
        if (guard.IsFailure)
            return guard;
        if (_request.ProfileUpdateStatus != FormProfileUpdateStatus.Processing)
            return Result.Success();

        var hasFailures = false;
        _outcomes.Clear();
        foreach (var outcome in outcomes)
        {
            _outcomes.Add(outcome);
            if (!outcome.Applied)
                hasFailures = true;
        }
        _request.ProfileUpdateStatus = hasFailures
            ? FormProfileUpdateStatus.PartiallyApplied
            : FormProfileUpdateStatus.Applied;
        _request.ProfileUpdateResolvedAtUtc = nowUtc;
        _request.Touch(nowUtc);
        return Result.Success();
    }

    // Hecho Rejected: nada se aplicó. Idempotente si ya se resolvió.
    public Result MarkRejected(Guid commandId, IReadOnlyCollection<FormProfileUpdateOutcome> failures, DateTime nowUtc)
    {
        var guard = EnsureCommand(commandId);
        if (guard.IsFailure)
            return guard;
        if (_request.ProfileUpdateStatus != FormProfileUpdateStatus.Processing)
            return Result.Success();

        _outcomes.Clear();
        foreach (var failure in failures)
            _outcomes.Add(failure with { Applied = false });
        _request.ProfileUpdateStatus = FormProfileUpdateStatus.Rejected;
        _request.ProfileUpdateResolvedAtUtc = nowUtc;
        _request.Touch(nowUtc);
        return Result.Success();
    }

    private Result EnsureCommand(Guid commandId) =>
        commandId != Guid.Empty && _request.ProfileUpdateCommandId == commandId
            ? Result.Success()
            : Result.Failure(FormRequestErrors.ProfileUpdateCommandMismatch);
}
