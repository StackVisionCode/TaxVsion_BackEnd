namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// Resolución por provider del <see cref="IOffSessionCharger"/>. Evita if/switch por string en
/// los handlers — cada provider se registra en DI con su propio nombre. Devuelve null cuando
/// el provider no está registrado o no soporta off-session (p.ej. Manual): el caller decide si
/// eso es error de negocio o de configuración.
/// </summary>
public interface IOffSessionChargerFactory
{
    IOffSessionCharger? For(string providerCode);
}
