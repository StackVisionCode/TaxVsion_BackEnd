using TaxVision.Tasks.ClientForms.Application.Templates.Commands;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class RenameFormTemplateHandlerTests
{
    [Fact]
    public async Task Rename_changes_metadata_and_saves()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var unit = new RecordingUnitOfWork();

        var result = await RenameFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, "Updated intake", "New description", []),
            new MemoryFormTemplateRepository(template), unit, default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated intake", template.Name.Value);
        Assert.Equal(1, unit.Saves);
    }
}
