namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cada vez que se recalcula el entitlement snapshot de un
/// tenant (alta, cambio de plan, suspensión/reactivación, compra de seats, add-ons).
/// CloudStorage, Auth, Notification, Signature, Communication, Planner y Email lo
/// consumen para refrescar los límites/flags que aplican localmente. Es el único evento
/// de "algo cambió en la suscripción" del bounded context — reemplaza a los antiguos
/// SubscriptionActivated/PlanChanged/Suspended/SeatsPurchased (retirados en la fase de
/// cleanup). <see cref="EntitlementValues"/> trae el snapshot resuelto en el mismo
/// instante en que se calculó, así que no hace falta una llamada HTTP adicional a
/// Subscription para reaccionar — consumidores más adelante pueden seguir prefiriendo un
/// GET /entitlements/summary si necesitan el estado más reciente en vez del embebido aquí.
/// </summary>
public sealed record TenantEntitlementsChangedIntegrationEvent : IntegrationEvent
{
    public required long RevisionNumber { get; init; }
    public required string[] ChangedKeys { get; init; }
    public required string PlanCode { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required int SeatCount { get; init; }
    public required int AvailableSeatCount { get; init; }

    /// <summary>
    /// Cupo efectivo de usuarios STAFF del tenant = `seats.max` incluido en el plan (3/10/25) + asientos
    /// STAFF (<c>SeatType.Standard</c>) no terminales comprados. Auth lo proyecta a
    /// <c>TenantPlanLimits.MaxUsers</c> y lo hace cumplir en <c>PlanGuard</c>. Distinto de
    /// <see cref="SeatCount"/> (todas las licencias de cualquier tipo). NON-required a propósito
    /// (evolución aditiva de un evento con ~7 consumidores): un mensaje viejo llega como null y Auth cae a
    /// <c>EntitlementValues["seats.max"]</c> — nunca a <see cref="SeatCount"/>, que arranca en 0.
    /// </summary>
    public int? MaxStaffUsers { get; init; }

    /// <summary>Snapshot resuelto completo: EntitlementKey -&gt; valor stringificado
    /// (ej. "seats.max" -&gt; "15", "storage.max_bytes" -&gt; "107374182400",
    /// "module.signatures" -&gt; "True"). Mismo contenido que expondría
    /// GET /entitlements/summary en el instante del recálculo.</summary>
    public required IReadOnlyDictionary<string, string> EntitlementValues { get; init; }
}
