using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates;

public sealed record VariableDefinitionInput(
    string Name,
    VariableType Type,
    bool Required,
    string? DefaultValue,
    string? Description
);

public sealed record VariableDefinitionResponse(
    string Name,
    string Type,
    bool Required,
    string? DefaultValue,
    string? Description
);

public sealed record EmailTemplateVersionResponse(
    Guid Id,
    int VersionNumber,
    string Status,
    string Subject,
    Guid LayoutId,
    int LayoutVersionNumber,
    IReadOnlyList<VariableDefinitionResponse> VariableDefinitions,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc
);

public sealed record EmailTemplateResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string TemplateKey,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<EmailTemplateVersionResponse> Versions,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public static class EmailTemplateMapper
{
    public static EmailTemplateResponse ToResponse(EmailTemplate template) =>
        new(
            template.Id,
            template.Scope.ToString(),
            template.TenantId,
            template.TemplateKey.Value,
            template.Name,
            template.Description,
            template.Status.ToString(),
            template.Versions.Select(ToVersionResponse).ToList(),
            template.CreatedAtUtc,
            template.UpdatedAtUtc
        );

    public static EmailTemplateVersionResponse ToVersionResponse(EmailTemplateVersion version) =>
        new(
            version.Id,
            version.VersionNumber,
            version.Status.ToString(),
            version.Subject,
            version.LayoutId,
            version.LayoutVersionNumber,
            version
                .VariableDefinitions.Select(d => new VariableDefinitionResponse(
                    d.Name,
                    d.Type.ToString(),
                    d.Required,
                    d.DefaultValue,
                    d.Description
                ))
                .ToList(),
            version.PublishedAtUtc,
            version.CreatedAtUtc
        );
}
