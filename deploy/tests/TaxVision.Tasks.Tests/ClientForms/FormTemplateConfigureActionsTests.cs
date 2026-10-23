using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormTemplateConfigureActionsTests
{
    [Fact]
    public async Task Changing_mappings_without_configure_actions_is_forbidden()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var unit = new RecordingUnitOfWork();

        var result = await UpdateAsync(
            template,
            FormMappingTestDefinitions.Onboarding,
            canConfigureActions: false,
            unit
        );

        Assert.Equal(FormTemplateErrors.ConfigureActionsRequired, result.Error);
        Assert.Equal(0, unit.Saves);
    }

    [Fact]
    public async Task Changing_mappings_with_configure_actions_is_saved()
    {
        var template = FormTemplateHandlerTestDoubles.Template();

        var result = await UpdateAsync(
            template,
            FormMappingTestDefinitions.Onboarding,
            canConfigureActions: true,
            new RecordingUnitOfWork()
        );

        Assert.True(result.Value.Updated);
        Assert.Contains("customer.primary_email", template.Draft.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_edits_keep_working_without_configure_actions_when_rules_do_not_change()
    {
        var template = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var retitled = FormMappingTestDefinitions.Onboarding.Replace(
            "\"title\":\"Client onboarding\"",
            "\"title\":\"New client onboarding\"",
            StringComparison.Ordinal
        );

        var result = await UpdateAsync(template, retitled, canConfigureActions: false, new RecordingUnitOfWork());

        Assert.True(result.Value.Updated);
        Assert.Contains("New client onboarding", template.Draft.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_an_action_without_configure_actions_is_forbidden()
    {
        var template = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var withoutAction =
            FormMappingTestDefinitions.Onboarding[
                ..FormMappingTestDefinitions.Onboarding.IndexOf("\"actions\":", StringComparison.Ordinal)
            ] + "\"actions\":[]}";

        var result = await UpdateAsync(template, withoutAction, canConfigureActions: false, new RecordingUnitOfWork());

        Assert.Equal(FormTemplateErrors.ConfigureActionsRequired, result.Error);
    }

    [Fact]
    public async Task Duplicating_a_template_with_rules_needs_configure_actions()
    {
        var source = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var templates = new MemoryFormTemplateRepository(source);

        var denied = await DuplicateAsync(source, templates, canConfigureActions: false);
        var allowed = await DuplicateAsync(source, templates, canConfigureActions: true);
        var plain = FormTemplateHandlerTestDoubles.Template();
        var plainCopy = await DuplicateAsync(
            plain,
            new MemoryFormTemplateRepository(plain),
            canConfigureActions: false
        );

        Assert.Equal(FormTemplateErrors.ConfigureActionsRequired, denied.Error);
        Assert.True(allowed.IsSuccess);
        Assert.True(plainCopy.IsSuccess);
    }

    [Fact]
    public async Task Reverting_to_a_version_with_other_rules_needs_configure_actions()
    {
        var stored = await PublishedVersionAsync();
        var versions = new MemoryFormTemplateVersionRepository(stored.Version);

        var denied = await RevertAsync(stored.Template, versions, canConfigureActions: false);
        var allowed = await RevertAsync(stored.Template, versions, canConfigureActions: true);

        Assert.Equal(FormTemplateErrors.ConfigureActionsRequired, denied.Error);
        Assert.True(allowed.IsSuccess);
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Templates.Responses.FormTemplateDetailResponse>> RevertAsync(
        FormTemplate template,
        MemoryFormTemplateVersionRepository versions,
        bool canConfigureActions
    ) =>
        RevertFormTemplateDraftHandler.Handle(
            new(
                template.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                template.Id,
                1,
                template.RowVersion,
                canConfigureActions
            ),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            versions,
            new RecordingUnitOfWork(),
            default
        );

    // Plantilla publicada con reglas cuyo borrador ya no las tiene.
    private static async Task<(
        FormTemplate Template,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion Version
    )> PublishedVersionAsync()
    {
        var template = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var versions = new MemoryFormTemplateVersionRepository();
        await FormMappingTestDefinitions.PublishAsync(template, FormTemplateHandlerTestDoubles.Publisher, versions);
        await UpdateAsync(
            template,
            FormTemplateHandlerTestDoubles.ValidDefinition,
            canConfigureActions: true,
            new RecordingUnitOfWork()
        );
        return (template, Assert.Single(versions.Items));
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Templates.Responses.UpdateFormTemplateDraftOutcome>> UpdateAsync(
        FormTemplate template,
        string definition,
        bool canConfigureActions,
        RecordingUnitOfWork unit
    ) =>
        UpdateFormTemplateDraftHandler.Handle(
            new(
                template.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                template.Id,
                definition,
                template.RowVersion,
                canConfigureActions
            ),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            unit,
            default
        );

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Templates.Responses.FormTemplateDetailResponse>> DuplicateAsync(
        FormTemplate source,
        MemoryFormTemplateRepository templates,
        bool canConfigureActions
    ) =>
        DuplicateFormTemplateHandler.Handle(
            new(source.TenantId, FormTemplateHandlerTestDoubles.UserId, source.Id, null, canConfigureActions),
            FormTemplateHandlerTestDoubles.Engine(),
            templates,
            new MemoryFormTemplateVersionRepository(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );
}
