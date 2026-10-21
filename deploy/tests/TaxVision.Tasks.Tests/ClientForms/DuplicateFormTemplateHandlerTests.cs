using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class DuplicateFormTemplateHandlerTests
{
    [Fact]
    public async Task Duplicate_preserves_component_ids_in_a_new_template()
    {
        var source = FormTemplateHandlerTestDoubles.Template();
        var templates = new MemoryFormTemplateRepository(source);
        var audit = new RecordingFormAuditLog();

        var result = await DuplicateFormTemplateHandler.Handle(
            new(source.TenantId, FormTemplateHandlerTestDoubles.UserId, source.Id, null),
            FormTemplateHandlerTestDoubles.Engine(), templates, new MemoryFormTemplateVersionRepository(), audit, new RecordingUnitOfWork(), default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, templates.Items.Count);
        Assert.Contains("fd_abcdefgh", templates.Items[1].Draft.Json);
        Assert.Equal([FormAuditAction.TemplateDuplicated], audit.Actions);
    }
}
