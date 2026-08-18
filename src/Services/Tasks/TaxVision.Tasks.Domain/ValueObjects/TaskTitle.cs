using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>Título de la tarea. Texto plano — lo que se ve en la bandeja y en el asunto del correo.</summary>
public sealed record TaskTitle
{
    public const int MaxLength = 200;

    public string Value { get; }

    private TaskTitle(string value) => Value = value;

    public static Result<TaskTitle> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<TaskTitle>(TaskErrors.TitleEmpty);

        var trimmed = value.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<TaskTitle>(TaskErrors.TitleTooLong)
            : Result.Success(new TaskTitle(trimmed));
    }

    public override string ToString() => Value;
}
