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

        var result = await ListFormTemplateVersionsHandler.Handle(
            new(template.TenantId, template.Id),
            new MemoryFormTemplateRepository(template),
            new MemoryFormTemplateVersionRepository(version),
            default
        );

        Assert.Single(result.Value);
        Assert.Equal(1, result.Value[0].VersionNumber);
    }

    [Fact]
    public async Task Missing_or_foreign_template_is_not_found_instead_of_an_empty_list()
    {
        var foreign = FormTemplateHandlerTestDoubles.Template(Guid.NewGuid());

        var missing = await ListFormTemplateVersionsHandler.Handle(
            new(FormTemplateHandlerTestDoubles.TenantId, Guid.NewGuid()),
            new MemoryFormTemplateRepository(foreign),
            new MemoryFormTemplateVersionRepository(),
            default
        );
        var other = await ListFormTemplateVersionsHandler.Handle(
            new(FormTemplateHandlerTestDoubles.TenantId, foreign.Id),
            new MemoryFormTemplateRepository(foreign),
            new MemoryFormTemplateVersionRepository(),
            default
        );

        Assert.Equal(FormTemplateErrors.NotFound, missing.Error);
        Assert.Equal(FormTemplateErrors.NotFound, other.Error);
    }
}
