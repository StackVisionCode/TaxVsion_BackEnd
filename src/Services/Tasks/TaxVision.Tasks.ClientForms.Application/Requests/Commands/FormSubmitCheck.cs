using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

// Respuestas ya depuradas y validadas para enviar, o los issues que lo impiden.
internal sealed record FormSubmitCheck(
    FormAnswers? Sanitized,
    IReadOnlyCollection<Guid> FileIds,
    IReadOnlyCollection<Guid> SensitiveIds,
    IReadOnlyList<ValidationIssue> Issues
)
{
    internal static FormSubmitCheck Invalid(IReadOnlyList<ValidationIssue> issues) => new(null, [], [], issues);
}
