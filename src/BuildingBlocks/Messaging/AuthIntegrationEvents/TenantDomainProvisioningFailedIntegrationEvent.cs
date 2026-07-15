namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando el provisioning en Cloudflare de un custom hostname
/// falla o es bloqueado — Fase A6. Cubre lo que el documento describe como
/// "CloudflareProvisioningFailed": Cloudflare es un detalle de implementación
/// detrás del ACL (ICloudflareProvisioningClient), así que el evento se nombra en
/// vocabulario propio (TenantDomain), no en el de Cloudflare.
/// </summary>
public sealed record TenantDomainProvisioningFailedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
    public required string Reason { get; init; }
}
