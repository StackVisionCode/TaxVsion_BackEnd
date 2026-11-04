namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>Estado del retiro según los acuses de cada servicio.</summary>
public enum OffboardingRunStatus
{
    /// <summary>Faltan acuses y todavía no venció el plazo.</summary>
    InProgress,

    /// <summary>Todos los pasos esperados acusaron.</summary>
    Completed,

    /// <summary>Venció el plazo con pasos sin acuse (ya se avisó a la oficina).</summary>
    Incomplete,
}
