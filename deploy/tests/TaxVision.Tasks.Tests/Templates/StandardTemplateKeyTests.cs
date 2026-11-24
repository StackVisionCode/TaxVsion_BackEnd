using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Tests.Templates;

/// <summary>
/// R1.8: las plantillas estándar se reconocen por clave, no por nombre. Install standard no duplica las que una oficina
/// tiene con el nombre antiguo; lista las que difieren de la versión vigente y solo las actualiza el administrador (D-23).
/// </summary>
public sealed class StandardTemplateKeyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task A_fresh_office_gets_the_three_with_their_keys_and_a_second_install_adds_nothing()
    {
        var templates = new InMemoryTaskTemplateRepository();

        var first = await InstallAsync(templates);
        templates.Items.Single(t => t.StandardKey == "941").Rename("Payroll 941 (ours)", null, DateTime.UtcNow);
        var second = await InstallAsync(templates);

        Assert.Equal(3, first.Value.Installed.Count);
        Assert.Equal(["1040", "1040-es", "941"], templates.Items.Select(t => t.StandardKey).Order());
        Assert.Empty(second.Value.Installed);
        Assert.Empty(second.Value.Outdated);
        Assert.Equal(3, templates.Items.Count);
    }

    [Fact]
    public async Task The_old_spanish_versions_are_adopted_not_duplicated_and_listed_as_outdated()
    {
        var old941 = Legacy(
            "941 — Declaración trimestral de nómina",
            "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=31",
            "Preparar y transmitir el 941"
        );
        var templates = new InMemoryTaskTemplateRepository(old941);

        var result = await InstallAsync(templates);

        Assert.Equal(2, result.Value.Installed.Count);
        Assert.Equal("941", old941.StandardKey);
        var outdated = Assert.Single(result.Value.Outdated);
        Assert.Equal((old941.Id, "941"), (outdated.TemplateId, outdated.StandardKey));
        Assert.Equal("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=31", old941.Recurrence!.Value);
    }

    [Fact]
    public async Task The_administrator_updates_an_outdated_one_to_the_current_version()
    {
        var old941 = Legacy(
            "941 — Declaración trimestral de nómina",
            "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=31",
            "Preparar y transmitir el 941"
        );
        old941.AdoptStandardKey("941");
        var templates = new InMemoryTaskTemplateRepository(old941);

        var updated = await UpdateStandardTaskTemplateHandler.Handle(
            new UpdateStandardTaskTemplateCommand(TenantId, old941.Id, Admin),
            templates,
            new RecordingUnitOfWork(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

        Assert.True(updated.IsSuccess);
        Assert.Equal("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1", old941.Recurrence!.Value);
        Assert.Equal("941 — Quarterly Payroll Return", old941.Name);
        Assert.Equal("Prepare and transmit the 941", Assert.Single(old941.Steps).Title.Value);
        Assert.Empty((await InstallAsync(templates)).Value.Outdated);
    }

    [Fact]
    public async Task A_template_that_is_not_standard_cannot_be_updated_as_one()
    {
        var own = Legacy("Our monthly close", "FREQ=MONTHLY;BYMONTHDAY=-1", "Close the books");

        var result = await UpdateStandardTaskTemplateHandler.Handle(
            new UpdateStandardTaskTemplateCommand(TenantId, own.Id, Admin),
            new InMemoryTaskTemplateRepository(own),
            new RecordingUnitOfWork(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

        Assert.Equal(TaskErrors.Template.NotStandard, result.Error);
    }

    private static Task<BuildingBlocks.Results.Result<StandardTemplatesInstallResponse>> InstallAsync(
        InMemoryTaskTemplateRepository templates
    ) =>
        InstallStandardTaskTemplatesHandler.Handle(
            new InstallStandardTaskTemplatesCommand(TenantId, Admin),
            templates,
            new RecordingUnitOfWork(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

    private static TaskTemplate Legacy(string name, string rule, string stepTitle)
    {
        var template = TaskTemplate.Create(TenantId, Admin, name, null, DateTime.UtcNow).Value;
        TaskTemplateStepFactory.ApplyRecurrence(template, rule, "America/New_York", RecurrenceMode.FixedSchedule);
        TaskTemplateStepFactory.ApplyTo(
            template,
            [new TaskTemplateStepDraft(1, stepTitle, null, TaskPriority.High, 2m, 0, true, null, null, null)]
        );
        return template;
    }
}
