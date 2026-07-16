namespace TaxVision.Subscription.Application.Subscriptions.Commands.Activate;

/// <summary>Self-service: el propio TenantAdmin decide pagar ya en vez de esperar a que
/// termine el trial. Convierte Trialing→Active y dispara un cobro real vía PaymentApp
/// (a diferencia de RenewTenantSubscriptionCommand, que es manual/PlatformAdmin y no cobra
/// de verdad todavía). <paramref name="BillingCycle"/> es opcional ("Monthly"/"Yearly"/etc,
/// string a parsear) — null mantiene el ciclo que ya tenía la suscripción (Monthly por
/// defecto, ver StartTrial).</summary>
public sealed record ActivateSubscriptionCommand(Guid TenantId, string? BillingCycle, Guid ActorUserId);
