using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class CreateFormTemplateHandlerTests
{
    [Fact]
    public async Task Create_adds_an_active_template_and_audit_in_one_save()
    {
        var templates = new MemoryFormTemplateRepository();
        var audit = new RecordingFormAuditLog();
        var unit = new RecordingUnitOfWork();

        var result = await CreateFormTemplateHandler.Handle(
            new(FormTemplateHandlerTestDoubles.TenantId, FormTemplateHandlerTestDoubles.UserId, "Annual intake", null, FormPurpose.ClientIntake, FormPresentation.Wizard),
            FormTemplateHandlerTestDoubles.Engine(), templates, audit, unit, default
        );

        Assert.True(result.IsSuccess);
        Assert.Single(templates.Items);
        Assert.Equal([FormAuditAction.TemplateCreated], audit.Actions);
        Assert.Equal(1, unit.Saves);
    }

    [Fact]
    public async Task Create_rejects_sensitive_data_in_description_without_saving()
    {
        var templates = new MemoryFormTemplateRepository();
        var audit = new RecordingFormAuditLog();
        var unit = new RecordingUnitOfWork();

        var result = await CreateFormTemplateHandler.Handle(
            new(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                "Annual intake",
                "Client tax ID 123-45-6789",
                FormPurpose.ClientIntake,
                FormPresentation.Wizard
            ),
            FormTemplateHandlerTestDoubles.Engine(),
            templates,
            audit,
            unit,
            default
        );

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
        Assert.Empty(templates.Items);
        Assert.Empty(audit.Actions);
        Assert.Equal(0, unit.Saves);
    }
}
