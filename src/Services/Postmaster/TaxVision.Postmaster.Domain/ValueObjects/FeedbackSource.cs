using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>De dónde vino un aviso de entrega. Solo el nombre: su vocabulario queda en el adaptador.</summary>
public sealed class FeedbackSource
{
    private const int MaxLength = 40;

    public static readonly FeedbackSource Smtp2Go = new("Smtp2Go");

    /// <summary>Rebote que llegó como correo (DSN/NDR) al buzón del tenant.</summary>
    public static readonly FeedbackSource Dsn = new("Dsn");

    public string Value { get; }

    private FeedbackSource(string value) => Value = value;

    public static Result<FeedbackSource> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return Result.Failure<FeedbackSource>(
                new Error(
                    "FeedbackSource.Invalid",
                    $"Feedback source is required and must be at most {MaxLength} chars."
                )
            );

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
                return Result.Failure<FeedbackSource>(
                    new Error("FeedbackSource.Invalid", "Feedback source must be letters and digits only.")
                );
        }

        return Result.Success(new FeedbackSource(value));
    }

    public override bool Equals(object? obj) => obj is FeedbackSource other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;
}
