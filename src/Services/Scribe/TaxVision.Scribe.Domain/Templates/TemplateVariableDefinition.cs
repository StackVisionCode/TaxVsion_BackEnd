using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.Templates;

public enum VariableType
{
    String,
    Number,
    Bool,
    Date,
    Url,
}

/// <summary>
/// Variable esperada por una EmailTemplateVersion — usada para rechazar placeholders no declarados al
/// publicar (validación en Application) y para poblar formularios de preview en el frontend.
/// </summary>
public sealed class TemplateVariableDefinition : BaseEntity
{
    private TemplateVariableDefinition() { }

    public Guid EmailTemplateVersionId { get; private set; }
    public string Name { get; private set; } = default!;
    public VariableType Type { get; private set; }
    public bool Required { get; private set; }
    public string? DefaultValue { get; private set; }
    public string? Description { get; private set; }

    internal static Result<TemplateVariableDefinition> Create(
        Guid emailTemplateVersionId,
        string name,
        VariableType type,
        bool required,
        string? defaultValue,
        string? description
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return Result.Failure<TemplateVariableDefinition>(
                new Error("TemplateVariableDefinition.Name", "Name is required and must be at most 100 characters.")
            );

        return Result.Success(
            new TemplateVariableDefinition
            {
                Id = Guid.NewGuid(),
                EmailTemplateVersionId = emailTemplateVersionId,
                Name = name.Trim(),
                Type = type,
                Required = required,
                DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            }
        );
    }
}
