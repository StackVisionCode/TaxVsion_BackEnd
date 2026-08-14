using TaxVision.Calendar.Domain.Scheduling;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// El id tiene que ser determinista: mover o cancelar una ocurrencia recalcula el mismo valor sin
/// consultar ningún mapa guardado. Si dejara de serlo, los recordatorios quedarían huérfanos y nadie
/// lo notaría hasta que un aviso no llegara.
/// </summary>
public sealed class OccurrenceTargetIdTests
{
    private static readonly Guid Appointment = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime Start = new(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_same_occurrence_always_yields_the_same_id()
    {
        Assert.Equal(OccurrenceTargetId.For(Appointment, Start), OccurrenceTargetId.For(Appointment, Start));
    }

    [Fact]
    public void Two_occurrences_of_the_same_series_get_different_ids()
    {
        // Un solo id por serie dispararía una vez y ya: es la razón de que el inicio entre en el hash.
        Assert.NotEqual(
            OccurrenceTargetId.For(Appointment, Start),
            OccurrenceTargetId.For(Appointment, Start.AddDays(7))
        );
    }

    [Fact]
    public void The_same_instant_on_two_series_gets_different_ids()
    {
        Assert.NotEqual(OccurrenceTargetId.For(Appointment, Start), OccurrenceTargetId.For(Guid.NewGuid(), Start));
    }

    [Fact]
    public void The_id_is_marked_as_a_derived_uuid()
    {
        // Se comprueba sobre la forma canonica y no sobre el arreglo de bytes: `ToByteArray` reordena
        // los tres primeros grupos y esconde en que indice quedo la version.
        var canonical = OccurrenceTargetId.For(Appointment, Start).ToString("D");

        Assert.Equal('5', canonical[14]);
        Assert.Contains(canonical[19], "89ab");
    }
}
