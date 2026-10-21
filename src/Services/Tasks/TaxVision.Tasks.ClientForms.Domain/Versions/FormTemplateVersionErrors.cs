using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Versions;

public static class FormTemplateVersionErrors
{
    public static readonly Error NotFound = new("FormTemplateVersion.NotFound", "The form template version was not found.");
}
