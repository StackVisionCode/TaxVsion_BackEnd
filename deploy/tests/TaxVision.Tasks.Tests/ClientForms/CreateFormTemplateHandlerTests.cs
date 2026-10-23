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
            new(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                "Annual intake",
                null,
                FormPurpose.ClientIntake,
                FormPresentation.Wizard
            ),
            FormTemplateHandlerTestDoubles.Engine(),
            templates,
            audit,
            unit,
            default
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

    [Theory]
    [InlineData(99, 1)]
    [InlineData(1, 99)]
    public async Task Create_rejects_unknown_purpose_or_presentation_before_canonicalizing(
        int purpose,
        int presentation
    )
    {
        var templates = new MemoryFormTemplateRepository();
        var unit = new RecordingUnitOfWork();

        var result = await CreateFormTemplateHandler.Handle(
            new(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                "Annual intake",
                null,
                (FormPurpose)purpose,
                (FormPresentation)presentation
            ),
            FormTemplateHandlerTestDoubles.Engine(),
            templates,
            new RecordingFormAuditLog(),
            unit,
            default
        );

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error,
            new[] { FormTemplateErrors.InvalidPurpose, FormTemplateErrors.InvalidPresentation }
        );
        Assert.Empty(templates.Items);
        Assert.Equal(0, unit.Saves);
    }
}
