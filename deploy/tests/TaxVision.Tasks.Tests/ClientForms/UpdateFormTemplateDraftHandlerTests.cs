using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class UpdateFormTemplateDraftHandlerTests
{
    [Fact]
    public async Task Unknown_definition_key_is_rejected_without_saving()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var unit = new RecordingUnitOfWork();

        var result = await UpdateFormTemplateDraftHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, "{\"schemaVersion\":1,\"unknown\":true}", []),
            FormTemplateHandlerTestDoubles.Engine(), new MemoryFormTemplateRepository(template), unit, default
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Updated);
        Assert.NotEmpty(result.Value.Issues);
        Assert.Equal(0, unit.Saves);
    }

    [Fact]
    public async Task Stale_row_version_returns_concurrency_conflict()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var result = await UpdateFormTemplateDraftHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, FormTemplateHandlerTestDoubles.ValidDefinition, [1]),
            FormTemplateHandlerTestDoubles.Engine(), new MemoryFormTemplateRepository(template), new RecordingUnitOfWork(), default
        );

        Assert.Equal(FormTemplateErrors.ConcurrencyConflict, result.Error);
    }
}
