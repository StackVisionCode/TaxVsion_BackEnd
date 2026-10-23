using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class DeleteFormTemplateHandlerTests
{
    [Fact]
    public async Task Delete_removes_an_unpublished_template_and_records_audit()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var templates = new MemoryFormTemplateRepository(template);
        var audit = new RecordingFormAuditLog();
        var unit = new RecordingUnitOfWork();

        var result = await DeleteFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id),
            templates,
            audit,
            unit,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Same(template, templates.Removed);
        Assert.Equal([FormAuditAction.TemplateDeleted], audit.Actions);
        Assert.Equal(1, unit.Saves);
    }
}
