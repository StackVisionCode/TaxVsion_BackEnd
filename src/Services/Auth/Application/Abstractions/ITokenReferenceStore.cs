namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Puente genérico entre un raw token generado en Auth y el consumidor que lo necesita
/// justo antes de usarlo, sin que el raw token viaje nunca por RabbitMQ. TTL corto (30s).
/// Originado en PayFlow (Fase 9) para el <c>RegistrationToken</c> de Onboarding; Fase 18 lo reusa
/// para el token de activación del TenantAdmin en <c>TenantCreatedIntegrationEvent</c> — vive en el
/// namespace Application-wide (no bajo Onboarding) precisamente porque ya no es exclusivo de ese
/// módulo.
/// <para>
/// Dos formas de lectura: <see cref="ConsumeAsync"/> borra la entrada atómicamente al leerla (GETDEL)
/// — correcto para secretos donde un segundo intento NUNCA debe devolver el valor
/// (<c>PasswordHashReference</c>, <c>AdminInvitationTokenReference</c>). <see cref="PeekAsync"/> lee
/// sin borrar, respetando el TTL original — correcto cuando el consumidor puede reintentar de forma
/// segura dentro de la misma ventana de exposición (auditoría F15: <c>RegistrationToken</c> resuelto
/// por Notification vía M2M — un fallo transient de Auth ya no debe perder el link de registro).
/// </para>
/// </summary>
public interface ITokenReferenceStore
{
    Task<Guid> StoreAsync(string rawToken, CancellationToken ct = default);
    Task StoreAsync(Guid reference, string rawToken, CancellationToken ct = default);
    Task<string?> ConsumeAsync(Guid reference, CancellationToken ct = default);
    Task<string?> PeekAsync(Guid reference, CancellationToken ct = default);
}
