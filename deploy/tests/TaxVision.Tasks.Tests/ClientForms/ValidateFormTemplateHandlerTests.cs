using TaxVision.Tasks.ClientForms.Application.Templates.Queries;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ValidateFormTemplateHandlerTests
{
    [Fact]
    public async Task Validate_returns_no_issues_for_a_publishable_definition()
    {
        var template = FormTemplateHandlerTestDoubles.Template();

        var result = await ValidateFormTemplateHandler.Handle(
            new(template.TenantId, template.Id),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Issues);
    }
}
