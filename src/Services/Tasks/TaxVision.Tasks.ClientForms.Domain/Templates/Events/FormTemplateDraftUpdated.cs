using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Templates.Events;

public sealed record FormTemplateDraftUpdated(Guid TemplateId, Guid TenantId, Guid ByUserId, DateTime OccurredAtUtc)
    : IDomainEvent;
