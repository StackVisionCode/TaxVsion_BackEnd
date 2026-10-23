namespace TaxVision.DataCollection.Engine.Validation;

public sealed record ValidationIssue(string Code, string Path, string? ComponentId, string? RowId, string Message);

public class ValidationReport
{
    public ValidationReport(IEnumerable<ValidationIssue>? issues = null) => Issues = issues?.ToArray() ?? [];

    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}

public sealed class AnswersValidationReport : ValidationReport
{
    public AnswersValidationReport(IEnumerable<ValidationIssue>? issues = null)
        : base(issues) { }
}

public enum AnswersValidationMode
{
    Draft,
    Submit,
}
