using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// Notification recibió en el webhook único del proveedor un aviso de un correo del sistema (Message-ID
/// <c>pm.</c>) y se lo pasa a Postmaster tal cual. Notification no lo interpreta: la traducción vive en
/// Postmaster. <see cref="IntegrationEvent.TenantId"/> = <c>PlatformTenant.Id</c>, porque el aviso no
/// dice de qué oficina es; Postmaster lo resuelve al atarlo a su correo.
/// </summary>
[MessageIdentity("notifications.email_provider_feedback_received.v1")]
public sealed record EmailProviderFeedbackReceivedIntegrationEvent : IntegrationEvent
{
    /// <summary>Origen del aviso, ej. "Smtp2Go". Postmaster elige el traductor con esto.</summary>
    public required string Source { get; init; }

    /// <summary>Un solo evento del proveedor, en JSON.</summary>
    public required string RawPayload { get; init; }

    public required DateTime ReceivedAtUtc { get; init; }
}
