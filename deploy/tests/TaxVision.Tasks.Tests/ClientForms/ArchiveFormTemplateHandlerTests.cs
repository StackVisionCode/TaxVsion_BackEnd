using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ArchiveFormTemplateHandlerTests
{
    [Fact]
    public async Task Archive_changes_status_and_records_audit()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var audit = new RecordingFormAuditLog();

        var result = await ArchiveFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, []),
            new MemoryFormTemplateRepository(template),
            audit,
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormTemplateStatus.Archived, template.Status);
        Assert.Equal([FormAuditAction.TemplateArchived], audit.Actions);
    }
}
