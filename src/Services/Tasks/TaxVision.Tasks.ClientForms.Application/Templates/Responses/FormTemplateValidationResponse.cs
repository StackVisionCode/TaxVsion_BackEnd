using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record FormTemplateValidationResponse(IReadOnlyList<ValidationIssue> Issues);
