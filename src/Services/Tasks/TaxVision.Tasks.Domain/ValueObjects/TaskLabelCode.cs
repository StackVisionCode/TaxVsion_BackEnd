using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// Identificador estable del label dentro del tenant. Es lo único que el front puede guardar: el
/// <c>DisplayName</c> se renombra y el <c>Id</c> no viaja en las URLs de configuración.
/// </summary>
public sealed partial record TaskLabelCode
{
    public const int MaxLength = 40;

    public string Value { get; }

    private TaskLabelCode(string value) => Value = value;

    public static Result<TaskLabelCode> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return Result.Failure<TaskLabelCode>(TaskErrors.Label.CodeEmpty);

        if (normalized.Length > MaxLength)
            return Result.Failure<TaskLabelCode>(TaskErrors.Label.CodeTooLong);

        return SlugPattern().IsMatch(normalized)
            ? Result.Success(new TaskLabelCode(normalized))
            : Result.Failure<TaskLabelCode>(TaskErrors.Label.CodeInvalid);
    }

    /// <summary>Los cinco estados del sistema: ningún estado de la oficina puede usar estos códigos.</summary>
    public static TaskLabelCode SystemCodeOf(TaskItemStatus category) =>
        new(
            category switch
            {
                TaskItemStatus.NotStarted => "not_started",
                TaskItemStatus.InProgress => "in_progress",
                TaskItemStatus.WaitingOnClient => "waiting_on_client",
                TaskItemStatus.Completed => "completed",
                TaskItemStatus.Cancelled => "cancelled",
                _ => throw new ArgumentOutOfRangeException(nameof(category)),
            }
        );

    public static bool IsReserved(TaskLabelCode code) => ReservedCodes.Contains(code.Value);

    /// <summary>
    /// Slug del nombre ("Under review" → <c>under_review</c>); si ya existe o es del sistema, se añade
    /// <c>_2</c>, <c>_3</c>… El código no cambia aunque luego se renombre el estado.
    /// </summary>
    public static TaskLabelCode FromDisplayName(string? displayName, IReadOnlySet<string> taken)
    {
        var slug = Slugify(displayName ?? string.Empty);
        var stem = slug.Length > MaxLength - 4 ? slug[..(MaxLength - 4)].TrimEnd('_') : slug;
        var candidate = stem;
        for (
            var n = 2;
            ReservedCodes.Contains(candidate) || RouteWords.Contains(candidate) || taken.Contains(candidate);
            n++
        )
            candidate = $"{stem}_{n}";
        return new TaskLabelCode(candidate);
    }

    private static readonly HashSet<string> ReservedCodes =
    [
        "not_started",
        "in_progress",
        "waiting_on_client",
        "completed",
        "cancelled",
    ];

    // Palabras de las rutas de estados (PUT /tasks/statuses/order): un código igual las taparía.
    private static readonly HashSet<string> RouteWords = ["order"];

    private static string Slugify(string name)
    {
        var plain = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToLowerInvariant(ch));

        var slug = NonSlugRun().Replace(plain.ToString(), "_").Trim('_');
        return slug.Length == 0 ? "status" : slug;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(_[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugRun();
}
