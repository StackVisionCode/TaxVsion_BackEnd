using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class RevertFormTemplateDraftHandlerTests
{
    [Fact]
    public async Task Revert_restores_the_selected_published_snapshot()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var parsed = engine.ParseDefinition(template.Draft.Json).Value;
        var version = template.Publish(engine.Canonicalize(parsed), parsed.Title, FormPresentation.Wizard, 1, FormTemplateHandlerTestDoubles.UserId, DateTime.UtcNow).Value;
        template.UpdateDraft(new("{\"changed\":true}"), TaxVision.Tasks.ClientForms.Domain.ValueObjects.ContentHash.Create(new string('a', 64)).Value, FormPurpose.Questionnaire, FormTemplateHandlerTestDoubles.UserId, DateTime.UtcNow);

        var result = await RevertFormTemplateDraftHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, version.VersionNumber, []),
            new MemoryFormTemplateRepository(template), new MemoryFormTemplateVersionRepository(version), new RecordingUnitOfWork(), default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(version.Definition.Json, template.Draft.Json);
    }
}
