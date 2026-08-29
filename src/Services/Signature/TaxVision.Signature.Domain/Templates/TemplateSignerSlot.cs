using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
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

    /// <summary>
    /// Verificación de identidad (OTP) que heredará el firmante al instanciar la plantilla.
    /// <c>null</c> = sin OTP. Nunca <see cref="SignerVerificationMethod.PractitionerPin"/> (ese es
    /// un gate a nivel de la solicitud, no por firmante).
    /// </summary>
    public SignerVerificationMethod? RequiredVerificationMethod { get; private set; }

    internal static Result<TemplateSignerSlot> Create(
        Guid templateId,
        int order,
        TemplateSlotRole role,
        string defaultLanguage,
        SignerVerificationMethod? requiredVerificationMethod = null
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

        if (requiredVerificationMethod == SignerVerificationMethod.PractitionerPin)
            return Result.Failure<TemplateSignerSlot>(
                new Error(
                    "Signature.TemplateSlot.VerificationMethod",
                    "PractitionerPin is a request-level gate, not a per-slot verification method."
                )
            );

        return Result.Success(
            new TemplateSignerSlot
            {
                Id = Guid.NewGuid(),
                SignatureTemplateId = templateId,
                Order = order,
                Role = role,
                DefaultLanguage = normalizedLanguage,
                RequiredVerificationMethod = requiredVerificationMethod,
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

    /// <summary>Edita rol, idioma y método de verificación del slot en sitio (no cambia el orden).</summary>
    internal Result Update(
        TemplateSlotRole role,
        string defaultLanguage,
        SignerVerificationMethod? requiredVerificationMethod
    )
    {
        ArgumentNullException.ThrowIfNull(role);

        var normalizedLanguage = NormalizeLanguage(defaultLanguage);
        if (normalizedLanguage is null)
            return Result.Failure(
                new Error("Signature.TemplateSlot.Language", "DefaultLanguage must be 'Es' or 'En'.")
            );

        if (requiredVerificationMethod == SignerVerificationMethod.PractitionerPin)
            return Result.Failure(
                new Error(
                    "Signature.TemplateSlot.VerificationMethod",
                    "PractitionerPin is a request-level gate, not a per-slot verification method."
                )
            );

        Role = role;
        DefaultLanguage = normalizedLanguage;
        RequiredVerificationMethod = requiredVerificationMethod;
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
