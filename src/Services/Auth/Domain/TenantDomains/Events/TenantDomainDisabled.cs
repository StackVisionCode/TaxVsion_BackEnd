using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.TenantDomains.Events;

/// <summary>Se emite al deshabilitar un dominio (nunca el primario — el agregado lo rechaza antes).</summary>
public sealed record TenantDomainDisabled(Guid TenantId, Guid DomainId, string Host, Guid? ActingUserId) : IDomainEvent;
