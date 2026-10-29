using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class GetFormTemplateHandlerTests
{
    [Fact]
    public async Task Get_returns_template_with_published_version_summaries()
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

        var result = await GetFormTemplateHandler.Handle(
            new(template.TenantId, template.Id),
            new MemoryFormTemplateRepository(template),
            new MemoryFormTemplateVersionRepository(version),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Versions);
    }
}
