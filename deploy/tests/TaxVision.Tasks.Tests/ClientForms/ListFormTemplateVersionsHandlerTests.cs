using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ListFormTemplateVersionsHandlerTests
{
    [Fact]
    public async Task List_versions_only_returns_the_requested_template()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var parsed = engine.ParseDefinition(template.Draft.Json).Value;
        var version = template.Publish(engine.Canonicalize(parsed), parsed.Title, FormPresentation.Wizard, 1, FormTemplateHandlerTestDoubles.UserId, DateTime.UtcNow).Value;

        var result = await ListFormTemplateVersionsHandler.Handle(
            new(template.TenantId, template.Id), new MemoryFormTemplateVersionRepository(version), default
        );

        Assert.Single(result);
        Assert.Equal(1, result[0].VersionNumber);
    }
}
