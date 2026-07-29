namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Puente genérico de un solo uso entre un raw token generado en Auth y el consumidor que
/// lo necesita justo antes de usarlo, sin que el raw token viaje nunca por RabbitMQ. TTL corto (30s)
/// y consumo atómico: <see cref="ConsumeAsync"/> borra la entrada al leerla. Originado en PayFlow
/// (Fase 9) para el <c>RegistrationToken</c> de Onboarding; Fase 18 lo reusa para el token de
/// activación del TenantAdmin en <c>TenantCreatedIntegrationEvent</c> — vive en el namespace
/// Application-wide (no bajo Onboarding) precisamente porque ya no es exclusivo de ese módulo.</summary>
public interface ITokenReferenceStore
{
    Task<Guid> StoreAsync(string rawToken, CancellationToken ct = default);
    Task<string?> ConsumeAsync(Guid reference, CancellationToken ct = default);
}
