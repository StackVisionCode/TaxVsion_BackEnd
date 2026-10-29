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
        TemplateChannel channel,
        Guid? tenantId,
        ITemplateStorageService storageService,
        EmailHtmlSafetyValidator htmlSafetyValidator,
        CancellationToken ct
    )
    {
        if (channel == TemplateChannel.Push)
            return await ValidatePushAsync(version, tenantId, storageService, ct);

        var htmlResult = await storageService.DownloadTextAsync(version.HtmlFileId!.Value, tenantId, ct);
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

    private static async Task<Result<TemplateValidationOutcome>> ValidatePushAsync(
        EmailTemplateVersion version,
        Guid? tenantId,
        ITemplateStorageService storageService,
        CancellationToken ct
    )
    {
        if (version.TextFileId is not { } textFileId)
            return Result.Failure<TemplateValidationOutcome>(
                new Error("EmailTemplateVersion.TextRequired", "Push templates require text content.")
            );
        var textResult = await storageService.DownloadTextAsync(textFileId, tenantId, ct);
        if (textResult.IsFailure)
            return Result.Failure<TemplateValidationOutcome>(textResult.Error);

        var declared = new HashSet<string>(
            version.VariableDefinitions.Select(d => d.Name),
            StringComparer.OrdinalIgnoreCase
        );
        var errors = new List<TemplateValidationIssue>();
        CheckUndeclaredPlaceholders(version.Subject, "subject", declared, errors);
        CheckUndeclaredPlaceholders(textResult.Value, "text", declared, errors);
        CheckPushSafety(version.Subject, textResult.Value, version.VariableDefinitions, errors);
        return Result.Success(new TemplateValidationOutcome(errors.Count == 0, errors, []));
    }

    // Largos con la rama más larga y cada variable a su máximo; un texto que no se puede acotar no se publica.
    private static void CheckPushSafety(
        string subject,
        string text,
        IEnumerable<TemplateVariableDefinition> variables,
        List<TemplateValidationIssue> errors
    )
    {
        var titleLength = PushTemplateLength.WorstCase(subject, variables);
        var bodyLength = PushTemplateLength.WorstCase(text, variables);
        if (titleLength is null || bodyLength is null)
            errors.Add(
                new(
                    "EmailTemplateVersion.PushUnbounded",
                    "Push templates can only use if, unless or case and variables with a declared maximum length."
                )
            );
        if (titleLength > 65)
            errors.Add(new("EmailTemplateVersion.PushTitleLength", "Push titles must be at most 65 characters."));
        if (bodyLength > 180)
            errors.Add(new("EmailTemplateVersion.PushBodyLength", "Push bodies must be at most 180 characters."));
        if (RawFilterPattern().IsMatch(subject) || RawFilterPattern().IsMatch(text))
            errors.Add(new("EmailTemplateVersion.PushRaw", "Push templates cannot use the raw filter."));
        if (UrlPattern().IsMatch(subject) || UrlPattern().IsMatch(text))
            errors.Add(new("EmailTemplateVersion.PushUrl", "Push templates cannot contain URLs."));
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

    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Con o sin espacios: "| raw", "|raw", "|  raw".
    [GeneratedRegex(@"\|\s*raw\b", RegexOptions.IgnoreCase)]
    private static partial Regex RawFilterPattern();
}
