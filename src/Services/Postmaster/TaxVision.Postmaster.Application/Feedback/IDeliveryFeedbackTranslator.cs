using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Traduce el payload de un proveedor (modo push: webhook) a nuestro idioma. Es el único lugar donde
/// se nombra el vocabulario del proveedor; vive en Infrastructure, uno por proveedor.
/// </summary>
public interface IDeliveryFeedbackTranslator
{
    FeedbackSource Source { get; }

    /// <summary>Falla si el payload no se puede leer. Un evento que no nos interesa simplemente no se devuelve.</summary>
    Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload);
}
