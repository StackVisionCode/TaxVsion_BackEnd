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

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(_[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
