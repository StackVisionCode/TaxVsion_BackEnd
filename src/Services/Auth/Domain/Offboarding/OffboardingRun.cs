using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>
/// Avance del retiro (offboard) de un usuario: qué pasos de qué servicios se esperan y cuáles ya acusaron.
/// Nace en el mismo comando que publica <c>UserOffboardedIntegrationEvent</c> (misma transacción), así que
/// todo acuse encuentra su corrida. El estado se deriva de los pasos: no hay carrera entre dos acuses
/// simultáneos (cada uno toca su fila) y un acuse tardío convierte un "Incomplete" en "Completed".
/// </summary>
public sealed class OffboardingRun : TenantEntity
{
    private readonly List<OffboardingRunStep> _steps = [];

    private OffboardingRun() { }

    [UserReference(UserReferenceKind.Provenance, Note = "Registro del retiro: prueba de lo que hizo cada servicio.")]
    public Guid UserId { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    // Ficha del retiro (ver OffboardingDetails). Null solo en corridas anteriores a la ficha.
    [UserReference(UserReferenceKind.Provenance, Note = "Quién hizo el retiro: historia para auditoría.")]
    public Guid? OffboardedByUserId { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "A quién pasó el trabajo: historia, no se reasigna.")]
    public Guid? SuccessorUserId { get; private set; }

    public OffboardingReason? Reason { get; private set; }
    public string? ReasonNote { get; private set; }

    /// <summary>Cuándo el barrido lo dio por incompleto y avisó (una sola vez).</summary>
    public DateTime? IncompleteAtUtc { get; private set; }

    public IReadOnlyList<OffboardingRunStep> Steps => _steps;

    public int ExpectedCount => _steps.Count;
    public int CompletedCount => _steps.Count(s => s.IsCompleted);
    public bool AllCompleted => _steps.All(s => s.IsCompleted);

    public OffboardingRunStatus Status =>
        AllCompleted ? OffboardingRunStatus.Completed
        : IncompleteAtUtc.HasValue ? OffboardingRunStatus.Incomplete
        : OffboardingRunStatus.InProgress;

    /// <summary>Fecha del último acuse cuando ya están todos.</summary>
    public DateTime? CompletedAtUtc => AllCompleted ? _steps.Max(s => s.CompletedAtUtc) ?? StartedAtUtc : null;

    public IEnumerable<OffboardingRunStep> PendingSteps => _steps.Where(s => !s.IsCompleted);

    public static Result<OffboardingRun> Start(
        Guid tenantId,
        Guid userId,
        DateTime startedAtUtc,
        OffboardingDetails details,
        IEnumerable<(string Service, string Step)> expectedSteps
    )
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return Result.Failure<OffboardingRun>(
                new Error("Offboarding.InvalidRun", "An offboarding run needs a tenant and a user.")
            );

        var run = new OffboardingRun
        {
            UserId = userId,
            StartedAtUtc = startedAtUtc,
            OffboardedByUserId = details.OffboardedByUserId,
            SuccessorUserId = details.SuccessorUserId,
            Reason = details.Reason,
            ReasonNote = details.ReasonNote,
        };
        run.SetTenant(tenantId);
        foreach (var (service, step) in expectedSteps.Distinct())
        {
            if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(step))
                return Result.Failure<OffboardingRun>(
                    new Error("Offboarding.InvalidStep", "Every expected offboarding step needs a service and a name.")
                );
            run._steps.Add(OffboardingRunStep.Expect(service, step));
        }

        return Result.Success(run);
    }

    /// <summary>
    /// Registra el acuse de un paso. Idempotente (repetido = éxito sin cambios). Un paso que no se esperaba
    /// falla: lo atrapa el fitness test del registro, y en vivo queda en el log en vez de inflar la cuenta.
    /// </summary>
    public Result<bool> CompleteStep(string service, string step, DateTime completedAtUtc)
    {
        var expected = _steps.FirstOrDefault(s => s.Matches(service, step));
        if (expected is null)
            return Result.Failure<bool>(
                new Error(
                    "Offboarding.UnexpectedStep",
                    $"Step '{service}:{step}' is not expected for this offboarding."
                )
            );
        return Result.Success(expected.Complete(completedAtUtc));
    }

    /// <summary>El barrido lo da por incompleto: solo si venció el plazo, faltan pasos y no se marcó antes.</summary>
    public Result MarkIncomplete(DateTime nowUtc, TimeSpan timeout)
    {
        if (AllCompleted)
            return Result.Failure(
                new Error("Offboarding.AlreadyCompleted", "Every offboarding step already confirmed.")
            );
        if (IncompleteAtUtc.HasValue)
            return Result.Failure(new Error("Offboarding.AlreadyIncomplete", "This offboarding was already flagged."));
        if (nowUtc - StartedAtUtc < timeout)
            return Result.Failure(new Error("Offboarding.NotDue", "This offboarding is still within its time limit."));

        IncompleteAtUtc = nowUtc;
        return Result.Success();
    }
}
