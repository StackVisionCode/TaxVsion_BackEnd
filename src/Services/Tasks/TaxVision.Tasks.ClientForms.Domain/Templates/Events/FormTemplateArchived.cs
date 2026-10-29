using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Templates.Events;

public sealed record FormTemplateArchived(Guid TemplateId, Guid TenantId, Guid ByUserId, DateTime OccurredAtUtc)
    : IDomainEvent;
