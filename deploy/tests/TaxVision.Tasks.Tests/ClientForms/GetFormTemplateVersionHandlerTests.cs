using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class GetFormTemplateVersionHandlerTests
{
    [Fact]
    public async Task Get_version_returns_the_immutable_definition()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var parsed = engine.ParseDefinition(template.Draft.Json).Value;
        var version = template
            .Publish(
                engine.Canonicalize(parsed),
                parsed.Title,
                FormPresentation.Wizard,
                1,
                FormTemplateHandlerTestDoubles.UserId,
                DateTime.UtcNow
            )
            .Value;

        var result = await GetFormTemplateVersionHandler.Handle(
            new(template.TenantId, template.Id, 1),
            new MemoryFormTemplateVersionRepository(version),
            default
        );

        Assert.Equal(version.Definition.Json, result.Value.Definition);
    }
}
