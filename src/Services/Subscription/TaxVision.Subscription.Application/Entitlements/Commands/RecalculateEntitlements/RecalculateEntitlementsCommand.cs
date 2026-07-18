namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;

/// <summary>
/// Se invoca tanto explícitamente (endpoint admin) como internamente, en proceso, al
/// final de los handlers que cambian algo que afecta al snapshot (plan, seats, add-ons).
/// No está registrado con PublishMessage, así que Wolverine lo resuelve localmente sin
/// pasar por RabbitMQ.
/// </summary>
public sealed record RecalculateEntitlementsCommand(Guid TenantId);
