using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Templates.Events;

public sealed record FormTemplateDraftReverted(Guid TemplateId, Guid TenantId, int VersionNumber, Guid ByUserId, DateTime OccurredAtUtc) : IDomainEvent;
