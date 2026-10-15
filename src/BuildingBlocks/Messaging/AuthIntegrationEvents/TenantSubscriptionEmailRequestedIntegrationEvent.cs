namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Solicitud de email de ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3). Auth
/// consume <c>TenantSubscriptionStatusChangedIntegrationEvent</c> (que trae solo TenantId), resuelve el
/// email del admin/owner del tenant desde SUS datos y publica esto para que Notification lo renderice y
/// envíe — mismo patrón que <c>OnboardingPaymentFailedNotificationRequestedIntegrationEvent</c> (Auth es
/// el dueño del contacto; Notification no conoce usuarios). El <see cref="Status"/> elige el template.
/// </summary>
public sealed record TenantSubscriptionEmailRequestedIntegrationEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Estado nuevo de la suscripción (PastDue/Suspended/Expired/Active…) — decide el template.</summary>
    public required string Status { get; init; }

    /// <summary>Motivo (SubscriptionChangeReason stringificado).</summary>
    public required string Reason { get; init; }

    /// <summary>Nombre del plan, para el copy.</summary>
    public string? PlanName { get; init; }

    /// <summary>Hasta cuándo llega el acceso ya pagado, cuando el tenant canceló al fin del período.</summary>
    public DateTime? AccessEndsAtUtc { get; init; }

    /// <summary>Código de fallo del proveedor, cuando la transición la disparó un pago fallido.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Días restantes para una renovación próxima (recordatorios 7/1).</summary>
    public int? DaysUntilRenewal { get; init; }

    /// <summary>Fecha de la próxima renovación (recordatorios pre-vencimiento).</summary>
    public DateTime? NextRenewalAtUtc { get; init; }

    /// <summary>URL a la que el email manda al admin para renovar/actualizar el pago (login de su oficina).</summary>
    public required string RenewUrl { get; init; }

    /// <summary>Importe formateado (p. ej. "49.00") para los emails de cobro recurrente.</summary>
    public string? Amount { get; init; }

    /// <summary>Últimos 4 del método guardado, para los emails de cobro recurrente/mandate.</summary>
    public string? CardLast4 { get; init; }

    /// <summary>Marca del método guardado (Visa/Mastercard/PayPal).</summary>
    public string? CardBrand { get; init; }

    /// <summary>URL del recibo (cuando aplica tras un cobro exitoso).</summary>
    public string? ReceiptUrl { get; init; }

    /// <summary>URL a Manage Subscription (p. ej. para "Switch back" o "Manage billing").</summary>
    public string? ManageUrl { get; init; }

    /// <summary>Modo nuevo cuando el user cambió preferencia (Automatic/Manual).</summary>
    public string? NewMode { get; init; }

    /// <summary>Cadencia del mandate (p. ej. "month"/"year").</summary>
    public string? Cadence { get; init; }

    /// <summary>Fecha del cobro ya formateada para los recurring_charge*.</summary>
    public string? DateCharged { get; init; }

    /// <summary>Fecha del próximo cobro ya formateada.</summary>
    public string? NextChargeDate { get; init; }
}
