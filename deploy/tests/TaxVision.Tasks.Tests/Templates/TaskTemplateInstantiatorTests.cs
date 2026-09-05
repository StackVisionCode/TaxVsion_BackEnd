using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Tests.Templates;

public sealed class TaskTemplateInstantiatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime DueAtUtc = new(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Instantiating_the_1040_creates_six_tasks_and_five_edges()
    {
        var result = Instantiator().Instantiate(Template1040(), Application());

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.Tasks.Count);
        Assert.Equal(5, result.Value.Dependencies.Count);
    }

    [Fact]
    public void Only_the_first_step_is_executable()
    {
        var result = Instantiator().Instantiate(Template1040(), Application());

        var executable = result.Value.Tasks.Where(t => !t.IsBlocked).ToList();

        Assert.Single(executable);
        Assert.Equal(FirstStepTitle(), executable[0].Title.Value);
    }

    [Fact]
    public void Due_dates_hang_off_the_engagement_due_date_by_their_offset()
    {
        var result = Instantiator().Instantiate(Template1040(), Application());

        var first = result.Value.Tasks.Single(t => t.Title.Value == FirstStepTitle());
        var last = result.Value.Tasks.Single(t => t.Title.Value == LastStepTitle());

        Assert.Equal(DueAtUtc.AddDays(-60), first.Due!.DueAtUtc);
        Assert.Equal(DueAtUtc, last.Due!.DueAtUtc);
    }

    [Fact]
    public void Every_task_carries_the_customer_and_the_template_it_came_from()
    {
        var template = Template1040();

        var result = Instantiator().Instantiate(template, Application());

        Assert.All(
            result.Value.Tasks,
            task =>
            {
                Assert.Equal(CustomerId, task.Reference.CustomerId);
                Assert.Equal(2025, task.Reference.TaxYear);
                Assert.Equal(template.Id, task.TemplateId);
            }
        );
    }

    /// <summary>
    /// Cada tarea con su propia instancia de referencia. Compartir una sola entre las seis compila y
    /// pasa cualquier assert de valor, pero EF Core sólo la persiste en una y las otras cinco pierden
    /// el cliente al guardar.
    /// </summary>
    [Fact]
    public void No_two_tasks_share_the_same_reference_instance()
    {
        var result = Instantiator().Instantiate(Template1040(), Application());

        var distinct = result.Value.Tasks.Select(t => t.Reference).Distinct(ReferenceEqualityComparer.Instance);

        Assert.Equal(result.Value.Tasks.Count, distinct.Count());
    }

    /// <summary>El 941 y el 1040-ES son series: un solo paso, y el grafo no los instancia.</summary>
    [Fact]
    public void The_standard_quarterly_templates_are_recurring_with_a_single_step()
    {
        var quarterly = StandardTaxTemplates.All.Where(t => t.RecurrenceRule is not null).ToList();

        Assert.Equal(2, quarterly.Count);
        Assert.All(quarterly, t => Assert.Single(t.Steps));
    }

    private static TaskTemplateInstantiator Instantiator() =>
        new(new InMemoryTaskRepository(), new InMemoryTaskDependencyRepository());

    private static TemplateApplication Application() =>
        new(UserId, UserId, CustomerId, 2025, DueAtUtc, "America/New_York", DateTime.UtcNow);

    private static TaskTemplate Template1040()
    {
        var standard = StandardTaxTemplates.All.Single(t => t.RecurrenceRule is null);
        var template = TaskTemplate.Create(TenantId, UserId, standard.Name, standard.Description, DateTime.UtcNow);

        var applied = TaskTemplateStepFactory.ApplyTo(template.Value, standard.Steps);
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error.Code : "");

        return template.Value;
    }

    // Los títulos esperados salen del propio seed, no de literales: así traducir o reescribir el
    // catálogo no rompe estos tests, que verifican el ORDEN y el mapeo, no el idioma. El texto en sí
    // lo fija StandardTaxTemplatesTests.
    private static string FirstStepTitle() => OrderedSteps().First().Title!;

    private static string LastStepTitle() => OrderedSteps().Last().Title!;

    private static IReadOnlyList<TaskTemplateStepDraft> OrderedSteps() =>
        StandardTaxTemplates.All.Single(t => t.RecurrenceRule is null).Steps.OrderBy(s => s.Order).ToList();
}
