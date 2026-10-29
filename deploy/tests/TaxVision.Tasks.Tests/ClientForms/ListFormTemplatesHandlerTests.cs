using TaxVision.Tasks.ClientForms.Application.Templates.Queries;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ListFormTemplatesHandlerTests
{
    [Fact]
    public async Task List_normalizes_page_and_page_size()
    {
        var result = await ListFormTemplatesHandler.Handle(
            new(FormTemplateHandlerTestDoubles.TenantId, null, null, null, 0, 999),
            new MemoryFormTemplateRepository(FormTemplateHandlerTestDoubles.Template()),
            default
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.Size);
        Assert.Single(result.Items);
    }
}
