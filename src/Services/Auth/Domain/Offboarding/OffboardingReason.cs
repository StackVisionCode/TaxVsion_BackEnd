namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>Motivo de la salida (opcional: es dato de RR. HH. y la oficina decide si lo registra).</summary>
public enum OffboardingReason
{
    Resigned,
    LetGo,
    ContractEnded,
    Other,
}
