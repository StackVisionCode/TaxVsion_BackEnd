namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Auditoría independiente post-Fase-9: el invariante §4 del plan de rate limiting exige rastro de
/// auditoría para categoría M "incluso al 429" — un intento bloqueado de una acción que mueve
/// dinero es en sí mismo una señal de seguridad (fuerza bruta, cuenta comprometida, automatización
/// descontrolada), no solo el intento exitoso. <see cref="RateLimitAttribute"/> invoca este sink
/// SOLO para políticas de <see cref="BuildingBlocks.RateLimiting.RateLimitCategory.M"/> — el resto
/// de categorías no lo necesitan (§4 solo lo exige para M).
///
/// <para>
/// Cada servicio con al menos una política M (hoy: Auth, PaymentApp) registra su propia
/// implementación sobre su mecanismo de auditoría existente (<c>IAuthAuditWriter</c>,
/// <c>IPaymentAuditLogWriter</c>) — <see cref="NoOpRateLimitAuditSink"/> es el default para el
/// resto, registrado vía <c>TryAddScoped</c> en <c>TieredRateLimitingRegistration</c>. No se
/// definió acá una tabla de auditoría genérica en BuildingBlocks porque cada servicio ya tiene la
/// suya con su propio esquema — duplicar esa tabla sería la misma sobre-ingeniería que ya se evitó
/// en la Fase 8 de Postmaster.
/// </para>
/// </summary>
public interface IRateLimitAuditSink
{
    Task OnBlockedAsync(RateLimitAuditContext context, CancellationToken ct = default);
}

public sealed record RateLimitAuditContext(
    Guid TenantId,
    Guid UserId,
    string PolicyName,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId
);

public sealed class NoOpRateLimitAuditSink : IRateLimitAuditSink
{
    public Task OnBlockedAsync(RateLimitAuditContext context, CancellationToken ct = default) => Task.CompletedTask;
}
