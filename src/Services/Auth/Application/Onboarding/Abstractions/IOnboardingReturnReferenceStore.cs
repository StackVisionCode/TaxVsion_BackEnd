namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Referencia de retorno OPACA que se incrusta en el successUrl del checkout. Al volver de Stripe, el
/// reconcile la usa para resolver el onboardingId cuando falta la cookie de sesión (otro navegador,
/// incógnito, cookies bloqueadas). NO es una credencial de registro: solo mapea referencia → onboardingId
/// para consultar estado; el token de registro de 72h sigue viajando únicamente por email. TTL corto.
/// </summary>
public interface IOnboardingReturnReferenceStore
{
    /// <summary>Emite una referencia opaca nueva atada al onboarding, válida por <paramref name="ttl"/>.
    /// Solo se persiste el hash de la referencia; el valor crudo (que va en la URL) nunca toca el store.</summary>
    Task<string> IssueAsync(Guid onboardingId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Resuelve una referencia a su onboardingId, o null si es inválida o expiró.</summary>
    Task<Guid?> ResolveAsync(string reference, CancellationToken ct = default);
}
