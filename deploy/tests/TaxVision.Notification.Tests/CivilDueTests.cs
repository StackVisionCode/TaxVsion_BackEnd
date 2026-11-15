using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

/// <summary>La fecha civil de un vencimiento en avisos y correos: con el corte de fin del día, el prefijo UTC ya no la dice.</summary>
public sealed class CivilDueTests
{
    [Fact]
    public void The_civil_date_wins_over_the_instant()
    {
        var text = CivilDue.Text(new DateOnly(2026, 11, 15), new DateTime(2026, 11, 16, 4, 59, 59, DateTimeKind.Utc));

        Assert.Equal("Nov 15, 2026", text);
    }

    /// <summary>Eventos de antes (sin fecha civil): medianoche UTC es esa fecha (P5).</summary>
    [Fact]
    public void An_older_event_reads_its_midnight_utc_as_that_date()
    {
        Assert.Equal(
            new DateOnly(2026, 11, 15),
            CivilDue.Of(null, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc))
        );
    }

    /// <summary>Fase 5: el vencimiento viaja como fecha civil tipada; Scribe la imprime en el idioma de quien lee.</summary>
    [Fact]
    public void A_typed_due_travels_as_a_civil_date()
    {
        var typed = CivilDue.Typed(new DateOnly(2026, 11, 15), new DateTime(2026, 11, 16, 4, 59, 59, DateTimeKind.Utc));

        Assert.Equal("2026-11-15", Assert.IsAssignableFrom<IDictionary<string, object?>>(typed)["$date"]);
    }

    [Fact]
    public void An_older_event_types_its_midnight_utc_as_that_date()
    {
        var typed = CivilDue.Typed(null, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("2026-11-15", Assert.IsAssignableFrom<IDictionary<string, object?>>(typed)["$date"]);
    }

    [Fact]
    public void Without_a_due_nothing_is_typed()
    {
        Assert.Null(CivilDue.Typed(null, null));
    }

    [Fact]
    public void Without_a_due_there_is_no_text()
    {
        Assert.Null(CivilDue.Text(null, null));
    }
}
