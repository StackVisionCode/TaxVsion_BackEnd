namespace TaxVision.Auth.Domain.Users;

/// <summary>Por qué se desactivó la cuenta. Restaurar el cliente solo devuelve la que cortó el archivado.</summary>
public enum UserDeactivationReason
{
    /// <summary>La desactivó la oficina a mano.</summary>
    Admin,

    /// <summary>La cortó el archivado del cliente (D-POR-1).</summary>
    CustomerArchived,

    /// <summary>Compensación de un onboarding cancelado.</summary>
    OnboardingCancelled,
}
