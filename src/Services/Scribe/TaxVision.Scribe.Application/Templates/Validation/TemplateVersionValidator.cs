using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Validation;

public sealed record TemplateValidationIssue(string Code, string Message);

public sealed record TemplateValidationOutcome(
    bool IsValid,
    IReadOnlyList<TemplateValidationIssue> Errors,
    IReadOnlyList<TemplateValidationIssue> Warnings
);

/// <summary>
/// Preflight de publish (Fase 5, plan §36 ítems 2 y 4): (a) todo placeholder Liquid en
/// HTML/text/subject debe estar declarado en <see cref="TemplateVariableDefinition"/>, y (b) el HTML
/// debe pasar <see cref="EmailHtmlSafetyValidator"/> (Fase 4.6). No re-parsea con Fluid — un regex sobre
/// el placeholder es suficiente para el criterio "detecta placeholder no declarado" del plan sin
/// necesitar un visitor de AST que Fluid.Core 2.31 no expone públicamente.
/// </summary>
public static partial class TemplateVersionValidator
{
    private static readonly HashSet<string> ReservedLiquidKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "forloop",
        "true",
        "false",
        "nil",
        "empty",
        "blank",
    };

    public static async Task<Result<TemplateValidationOutcome>> ValidateAsync(
        EmailTemplateVersion version,
        Guid? tenantId,
        ITemplateStorageService storageService,
        EmailHtmlSafetyValidator htmlSafetyValidator,
        CancellationToken ct
    )
    {
        var htmlResult = await storageService.DownloadTextAsync(version.HtmlFileId, tenantId, ct);
        if (htmlResult.IsFailure)
            return Result.Failure<TemplateValidationOutcome>(htmlResult.Error);

        string? textSource = null;
        if (version.TextFileId is { } textFileId)
        {
            var textResult = await storageService.DownloadTextAsync(textFileId, tenantId, ct);
            if (textResult.IsFailure)
                return Result.Failure<TemplateValidationOutcome>(textResult.Error);
            textSource = textResult.Value;
        }

        var declaredNames = new HashSet<string>(
            version.VariableDefinitions.Select(d => d.Name),
            StringComparer.OrdinalIgnoreCase
        );

        var errors = new List<TemplateValidationIssue>();
        var warnings = new List<TemplateValidationIssue>();

        CheckUndeclaredPlaceholders(version.Subject, "subject", declaredNames, errors);
        CheckUndeclaredPlaceholders(htmlResult.Value, "html", declaredNames, errors);
        if (textSource is not null)
            CheckUndeclaredPlaceholders(textSource, "text", declaredNames, errors);

        var safetyOutcome = htmlSafetyValidator.Validate(htmlResult.Value);
        errors.AddRange(safetyOutcome.Errors.Select(e => new TemplateValidationIssue(e.Code, e.Message)));
        warnings.AddRange(safetyOutcome.Warnings.Select(w => new TemplateValidationIssue(w.Code, w.Message)));

        return Result.Success(new TemplateValidationOutcome(errors.Count == 0, errors, warnings));
    }

    private static void CheckUndeclaredPlaceholders(
        string source,
        string sourceName,
        HashSet<string> declaredNames,
        List<TemplateValidationIssue> errors
    )
    {
        var undeclared = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ExtractPlaceholderNames(source))
            if (!declaredNames.Contains(name) && !ReservedLiquidKeywords.Contains(name))
                undeclared.Add(name);

        foreach (var name in undeclared)
            errors.Add(
                new TemplateValidationIssue(
                    "EmailTemplateVersion.UndeclaredVariable",
                    $"Variable '{name}' is used in the {sourceName} but is not declared in VariableDefinitions."
                )
            );
    }

    private static IEnumerable<string> ExtractPlaceholderNames(string source)
    {
        foreach (Match match in OutputVariablePattern().Matches(source))
            yield return match.Groups[1].Value;

        foreach (Match match in ConditionVariablePattern().Matches(source))
            yield return match.Groups[1].Value;

        foreach (Match match in ForVariablePattern().Matches(source))
            yield return match.Groups[1].Value;
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)")]
    private static partial Regex OutputVariablePattern();

    [GeneratedRegex(@"\{%\s*(?:if|elsif|unless)\s+([a-zA-Z_][a-zA-Z0-9_]*)")]
    private static partial Regex ConditionVariablePattern();

    [GeneratedRegex(@"\{%\s*for\s+\w+\s+in\s+([a-zA-Z_][a-zA-Z0-9_]*)")]
    private static partial Regex ForVariablePattern();
}
