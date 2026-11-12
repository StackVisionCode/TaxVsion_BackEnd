namespace TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;

/// <summary>CAT-F2.6 — autoría del multiplicador de rate-limit de un plan para una categoría. Si la
/// fila (planCode, category) ya existe la actualiza; si no, la crea. Esto permite que un plan nuevo
/// creado en F2.3 reciba multiplicadores sin tocar código. D12/D13: bajar el cupo (multiplicador menor o cupo
/// fijo nuevo, menor o quitado) exige el token de la vista previa y un motivo.</summary>
public sealed record UpsertPlanRateLimitCommand(
    string PlanCode,
    string Category,
    decimal MultiplierOverride,
    int? HardOverridePerMinute,
    Guid ActorUserId,
    string? ConfirmationToken = null,
    string? Reason = null
);
