using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Domain.Templates;

/// <summary>
/// Slot de firmante en una plantilla. Representa un puesto que se rellenará con datos
/// reales (email + nombre) al instanciar la plantilla. Entidad interna del aggregate;
/// sólo se crea/muta via métodos del root <see cref="SignatureTemplate"/>.
/// </summary>
public sealed class TemplateSignerSlot : BaseEntity
{
    private TemplateSignerSlot() { }

    public Guid SignatureTemplateId { get; private set; }

    /// <summary>Orden 1-indexado. Cuando la plantilla es secuencial, define el turno.</summary>
    public int Order { get; private set; }

    public TemplateSlotRole Role { get; private set; } = default!;

    /// <summary>Idioma sugerido para el correo de invitación al firmante ("Es" | "En").</summary>
    public string DefaultLanguage { get; private set; } = default!;

    internal static Result<TemplateSignerSlot> Create(
        Guid templateId,
        int order,
        TemplateSlotRole role,
        string defaultLanguage
    )
    {
        if (templateId == Guid.Empty)
            return Result.Failure<TemplateSignerSlot>(
                new Error("Signature.TemplateSlot.Template", "TemplateId is required.")
            );
        if (order < 1)
            return Result.Failure<TemplateSignerSlot>(
                new Error("Signature.TemplateSlot.Order", "Slot order must be >= 1.")
            );
        ArgumentNullException.ThrowIfNull(role);

        var normalizedLanguage = NormalizeLanguage(defaultLanguage);
        if (normalizedLanguage is null)
            return Result.Failure<TemplateSignerSlot>(
                new Error("Signature.TemplateSlot.Language", "DefaultLanguage must be 'Es' or 'En'.")
            );

        return Result.Success(
            new TemplateSignerSlot
            {
                Id = Guid.NewGuid(),
                SignatureTemplateId = templateId,
                Order = order,
                Role = role,
                DefaultLanguage = normalizedLanguage,
            }
        );
    }

    internal Result Reorder(int newOrder)
    {
        if (newOrder < 1)
            return Result.Failure(new Error("Signature.TemplateSlot.Order", "Slot order must be >= 1."));
        Order = newOrder;
        return Result.Success();
    }

    private static string? NormalizeLanguage(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        var lower = candidate.Trim().ToLowerInvariant();
        return lower switch
        {
            "es" => "Es",
            "en" => "En",
            _ => null,
        };
    }
}
