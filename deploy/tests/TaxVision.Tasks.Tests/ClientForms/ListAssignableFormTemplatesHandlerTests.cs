using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ListAssignableFormTemplatesHandlerTests
{
    [Fact]
    public async Task Assignable_list_only_returns_published_active_templates()
    {
        var published = FormTemplateHandlerTestDoubles.Template();
        var draft = FormTemplateHandlerTestDoubles.Template();
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var parsed = engine.ParseDefinition(published.Draft.Json).Value;
        published.Publish(
            engine.Canonicalize(parsed),
            parsed.Title,
            FormPresentation.Wizard,
            1,
            FormTemplateHandlerTestDoubles.UserId,
            DateTime.UtcNow
        );

        var result = await ListAssignableFormTemplatesHandler.Handle(
            new(FormTemplateHandlerTestDoubles.TenantId, null),
            new MemoryFormTemplateRepository(published, draft),
            default
        );

        Assert.Single(result);
        Assert.Equal(published.Id, result[0].TemplateId);
    }
}
