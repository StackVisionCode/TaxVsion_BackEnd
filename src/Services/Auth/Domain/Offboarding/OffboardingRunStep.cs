namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>
/// Un paso esperado del retiro: un consumer de un servicio. Se crea con el <see cref="OffboardingRun"/> (la
/// lista vive con la corrida, así un cambio de registro no altera retiros viejos) y se marca al llegar el acuse.
/// </summary>
public sealed class OffboardingRunStep
{
    public const int ServiceMaxLength = 64;
    public const int StepMaxLength = 128;

    private OffboardingRunStep() { }

    // Id vacío: lo asigna EF al insertar (una hija con Id preasignado se rastrearía como UPDATE).
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public string Service { get; private set; } = default!;
    public string Step { get; private set; } = default!;
    public DateTime? CompletedAtUtc { get; private set; }

    public bool IsCompleted => CompletedAtUtc.HasValue;

    internal static OffboardingRunStep Expect(string service, string step) => new() { Service = service, Step = step };

    /// <summary>Idempotente: el primer acuse fija la fecha; los repetidos no la cambian.</summary>
    internal bool Complete(DateTime completedAtUtc)
    {
        if (CompletedAtUtc.HasValue)
            return false;
        CompletedAtUtc = completedAtUtc;
        return true;
    }

    public bool Matches(string service, string step) =>
        string.Equals(Service, service, StringComparison.Ordinal)
        && string.Equals(Step, step, StringComparison.Ordinal);
}
