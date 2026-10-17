using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Infrastructure.Persistence.Converters;

// Resiliente a filas legacy "Standard" (renombrado F5.3 a Starter). Al guardar escribe siempre el
// nombre canónico; al leer traduce aliases antes de parsear. Evita que una fila huérfana tumbe el
// reconciler en cualquier entorno. Nuevos renombres: añadir aquí la pareja alias→canonical.
public sealed class PlanTierConverter : ValueConverter<PlanTier, string>
{
    public PlanTierConverter()
        : base(tier => tier.ToString(), raw => FromRaw(raw)) { }

    public static PlanTier FromRaw(string raw)
    {
        var normalized = raw switch
        {
            "Standard" => nameof(PlanTier.Starter),
            _ => raw,
        };
        return Enum.Parse<PlanTier>(normalized, ignoreCase: true);
    }
}
