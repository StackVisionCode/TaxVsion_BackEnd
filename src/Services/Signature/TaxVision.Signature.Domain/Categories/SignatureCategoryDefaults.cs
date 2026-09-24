using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Domain.Categories;

/// <summary>
/// Categorías de sistema (los valores fijos de <see cref="SignatureCategory"/>). Siempre disponibles
/// para todos los tenants; las categorías custom del tenant no pueden duplicar sus nombres.
/// </summary>
public static class SignatureCategoryDefaults
{
    public static readonly IReadOnlyList<string> Names = Enum.GetNames<SignatureCategory>();
}
