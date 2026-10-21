using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Templates.Events;

public sealed record FormTemplateRestored(Guid TemplateId, Guid TenantId, Guid ByUserId, DateTime OccurredAtUtc) : IDomainEvent;
