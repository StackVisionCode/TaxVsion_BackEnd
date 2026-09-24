using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Analytics;

/// <summary>
/// Analytics agrupa por el enum de categorías de sistema; una categoría custom del tenant se cuenta
/// bajo <see cref="SignatureCategory.Other"/> (no rompe el grano histórico del snapshot).
/// </summary>
public static class SignatureCategoryParsing
{
    public static SignatureCategory ToSystemCategory(string category) =>
        Enum.TryParse<SignatureCategory>(category, ignoreCase: true, out var parsed) ? parsed : SignatureCategory.Other;
}
