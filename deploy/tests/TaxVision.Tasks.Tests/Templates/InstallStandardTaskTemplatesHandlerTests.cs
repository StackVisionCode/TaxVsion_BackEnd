using TaxVision.Tasks.Application.Templates.Commands;

namespace TaxVision.Tasks.Tests.Templates;

public sealed class InstallStandardTaskTemplatesHandlerTests
{
    /// <summary>P24: las fechas del catálogo se cuentan en la zona de la oficina, no en Nueva York fijo.</summary>
    [Fact]
    public async Task The_recurring_templates_are_installed_in_the_office_time_zone()
    {
        var templates = new InMemoryTaskTemplateRepository();

        var result = await InstallStandardTaskTemplatesHandler.Handle(
            new InstallStandardTaskTemplatesCommand(Guid.NewGuid(), Guid.NewGuid()),
            templates,
            new RecordingUnitOfWork(),
            new FixedOfficeZone("America/Chicago"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var recurring = templates.Items.Where(t => t.Recurrence is not null).ToList();
        Assert.Equal(2, recurring.Count);
        Assert.All(recurring, t => Assert.Equal("America/Chicago", t.Recurrence!.TimeZoneId));
    }
}
