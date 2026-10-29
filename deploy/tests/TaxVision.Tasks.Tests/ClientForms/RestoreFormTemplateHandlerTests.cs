using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class RestoreFormTemplateHandlerTests
{
    [Fact]
    public async Task Restore_reactivates_an_archived_template()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        template.Archive(FormTemplateHandlerTestDoubles.UserId, DateTime.UtcNow);
        var audit = new RecordingFormAuditLog();

        var result = await RestoreFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, []),
            new MemoryFormTemplateRepository(template),
            audit,
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormTemplateStatus.Active, template.Status);
        Assert.Equal([FormAuditAction.TemplateRestored], audit.Actions);
    }
}
