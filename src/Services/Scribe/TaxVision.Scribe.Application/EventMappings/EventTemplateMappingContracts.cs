using TaxVision.Scribe.Domain.EventMappings;

namespace TaxVision.Scribe.Application.EventMappings;

public sealed record EventTemplateMappingResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string EventKey,
    string TemplateKey,
    string? Locale,
    int Priority,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public static class EventTemplateMappingMapper
{
    public static EventTemplateMappingResponse ToResponse(EventTemplateMapping mapping) =>
        new(
            mapping.Id,
            mapping.Scope.ToString(),
            mapping.TenantId,
            mapping.EventKey.Value,
            mapping.TemplateKey.Value,
            mapping.Locale?.Value,
            mapping.Priority,
            mapping.Enabled,
            mapping.CreatedAtUtc,
            mapping.UpdatedAtUtc
        );
}
