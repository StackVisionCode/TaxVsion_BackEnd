using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Tests.Templates;

/// <summary>
/// Contrato del catálogo estándar. Fija lo que NO puede cambiar por accidente:
/// los NOMBRES son la clave de idempotencia del install (InstallStandardTaskTemplatesHandler salta por
/// Name), así que renombrar uno hace que los tenants que YA instalaron reciban un duplicado al
/// reinstalar. Si este test se pone rojo por un rename, esa es la conversación a tener —decidir la
/// reconciliación de datos—, no "arreglar el assert". También ancla la forma del 1040 (cadena de 6) y
/// de las dos series trimestrales, para que otros tests puedan derivar títulos del seed sin fijar idioma.
/// </summary>
public sealed class StandardTaxTemplatesTests
{
    [Fact]
    public void The_catalog_has_exactly_the_three_expected_templates_by_name()
    {
        var names = StandardTaxTemplates.All.Select(t => t.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "1040 — Individual Return",
                "1040-ES — Quarterly Estimated Payments",
                "941 — Quarterly Payroll Return",
            },
            names
        );
    }

    [Fact]
    public void Every_step_of_every_template_has_a_non_empty_title()
    {
        var blank = StandardTaxTemplates
            .All.SelectMany(t => t.Steps)
            .Where(s => string.IsNullOrWhiteSpace(s.Title))
            .ToList();

        Assert.Empty(blank);
    }

    [Fact]
    public void The_1040_is_a_six_step_chain_ending_in_a_statutory_e_file()
    {
        var individual = StandardTaxTemplates.All.Single(t => t.RecurrenceRule is null);

        Assert.Equal(RecurrenceMode.FixedSchedule, individual.RecurrenceMode);

        var steps = individual.Steps.OrderBy(s => s.Order).ToList();
        Assert.Equal(6, steps.Count);
        Assert.Equal(Enumerable.Range(1, 6), steps.Select(s => s.Order));

        // Cadena estricta: el primero arranca solo; cada paso siguiente depende del anterior.
        Assert.Null(steps[0].DependsOnStepOrder);
        for (var i = 1; i < steps.Count; i++)
            Assert.Equal(steps[i - 1].Order, steps[i].DependsOnStepOrder);

        // El terminal es la transmisión y es fecha legal; ninguno de los previos lo es.
        Assert.True(steps[^1].IsStatutory);
        Assert.All(steps.Take(steps.Count - 1), s => Assert.False(s.IsStatutory));

        // Los vencimientos van del más temprano (negativo) al día del encargo (0), sin pasarse.
        Assert.All(steps, s => Assert.True(s.DueOffsetDays <= 0));
        Assert.Equal(0, steps[^1].DueOffsetDays);
    }

    [Fact]
    public void The_two_quarterly_series_have_a_recurrence_rule_and_a_single_statutory_step()
    {
        var series = StandardTaxTemplates.All.Where(t => t.RecurrenceRule is not null).ToList();

        Assert.Equal(2, series.Count);
        Assert.All(
            series,
            t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t.RecurrenceRule));
                Assert.Single(t.Steps);
                Assert.True(t.Steps[0].IsStatutory);
            }
        );
    }
}
