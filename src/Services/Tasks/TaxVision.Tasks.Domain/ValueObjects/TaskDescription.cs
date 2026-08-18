using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// Detalle libre en texto plano. No pasa por sanitizador porque, a diferencia de
/// <see cref="ClientRequestNote"/>, nunca sale del servicio hacia un correo.
/// </summary>
public sealed record TaskDescription
{
    public const int MaxLength = 8_000;

    public string Value { get; }

    private TaskDescription(string value) => Value = value;

    public static Result<TaskDescription> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<TaskDescription>(TaskErrors.DescriptionEmpty);

        var trimmed = value.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<TaskDescription>(TaskErrors.DescriptionTooLong)
            : Result.Success(new TaskDescription(trimmed));
    }

    public override string ToString() => Value;
}
