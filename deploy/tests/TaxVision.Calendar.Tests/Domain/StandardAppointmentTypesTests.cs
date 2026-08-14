using TaxVision.Calendar.Application.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class StandardAppointmentTypesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_four_starter_types_build_for_a_tenant()
    {
        var types = StandardAppointmentTypes.Build(Guid.NewGuid(), Now);

        Assert.True(types.IsSuccess);
        Assert.Equal(4, types.Value.Count);
        Assert.All(types.Value, type => Assert.True(type.IsActive));
    }

    [Fact]
    public void Only_the_signature_blocks_on_conflict()
    {
        // Firmar exige estar presente: estar en dos sitios no es un aviso que se pueda ignorar.
        var types = StandardAppointmentTypes.Build(Guid.NewGuid(), Now).Value;

        var blocking = types.Where(type => type.BlocksOnConflict).ToArray();

        Assert.Single(blocking);
        Assert.Equal("Firma", blocking[0].Name);
    }

    [Fact]
    public void The_document_drop_off_is_the_one_with_a_daily_cap()
    {
        // Es la que se pide en cadena en temporada: sin tope, un preparador acepta catorce el 10 de abril.
        var types = StandardAppointmentTypes.Build(Guid.NewGuid(), Now).Value;

        var capped = types.Where(type => type.DailyCap is not null).ToArray();

        Assert.Single(capped);
        Assert.Equal("Entrega de documentos", capped[0].Name);
        Assert.Equal(12, capped[0].DailyCap);
    }
}
