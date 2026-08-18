using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Types;

/// <summary>
/// Los cuatro tipos con que arranca una firma fiscal. No son ejemplos: son el reparto real del dia de
/// un preparador, y tenerlos el primer dia evita que cada oficina invente su propio catalogo.
/// </summary>
public static class StandardAppointmentTypes
{
    public static Result<IReadOnlyList<AppointmentType>> Build(Guid tenantId, DateTime nowUtc)
    {
        var definitions = new (string Name, int Minutes, string Color, bool Virtual, bool Blocks, int? Cap)[]
        {
            ("Consulta inicial", 30, "#2563EB", true, false, null),
            ("Entrega de documentos", 15, "#16A34A", false, false, 12),
            ("Revision de declaracion", 60, "#CA8A04", true, false, null),
            // La unica que bloquea: firmar exige estar presente, y estar en dos sitios no es un aviso.
            ("Firma", 30, "#DC2626", false, true, null),
        };

        var types = new List<AppointmentType>();
        foreach (var (name, minutes, color, isVirtual, blocks, cap) in definitions)
        {
            var type = AppointmentType.Create(
                tenantId,
                name,
                TimeSpan.FromMinutes(minutes),
                color,
                nowUtc,
                isVirtual,
                blocks,
                cap
            );

            if (type.IsFailure)
                return Result.Failure<IReadOnlyList<AppointmentType>>(type.Error);

            types.Add(type.Value);
        }

        return Result.Success<IReadOnlyList<AppointmentType>>(types);
    }
}
