using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class PublishFormTemplateHandlerTests
{
    [Fact]
    public async Task Publish_creates_an_immutable_version_and_audit()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var versions = new MemoryFormTemplateVersionRepository();
        var audit = new RecordingFormAuditLog();
        var unit = new RecordingUnitOfWork();

        var result = await PublishFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, []),
            FormTemplateHandlerTestDoubles.Engine(), new MemoryFormTemplateRepository(template), versions, audit, unit, default
        );

        Assert.True(result.Value.Published);
        Assert.Single(versions.Items);
        Assert.Equal([FormAuditAction.TemplatePublished], audit.Actions);
        Assert.Equal(1, unit.Saves);
    }
}
