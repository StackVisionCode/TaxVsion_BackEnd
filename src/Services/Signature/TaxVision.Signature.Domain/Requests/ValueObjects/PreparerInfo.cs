using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Identidad del preparer que aparece en el documento (Form 8879 §V, POA §Section 3,
/// engagement letters). El PTIN o EFIN es el identificador oficial del profesional
/// tributario según IRS Publication 1345 y §6109(a)(4).
///
/// <para>
/// El VO valida formato del identificador y longitudes. La firma real del preparer
/// se ejecuta con las credenciales del usuario staff autenticado, no con token público
/// — nunca sale del microservicio.
/// </para>
/// </summary>
public sealed record PreparerInfo
{
    public const int MinIdentifierLength = 6;
    public const int MaxIdentifierLength = 20;
    public const int MinDisplayNameLength = 3;
    public const int MaxDisplayNameLength = 200;
    public const int MaxTitleLabelLength = 100;

    public string PtinOrEfin { get; }
    public string DisplayName { get; }
    public string? TitleLabel { get; }

    private PreparerInfo(string ptinOrEfin, string displayName, string? titleLabel)
    {
        PtinOrEfin = ptinOrEfin;
        DisplayName = displayName;
        TitleLabel = titleLabel;
    }

    public static Result<PreparerInfo> Create(string? ptinOrEfin, string? displayName, string? titleLabel)
    {
        var idResult = ValidateIdentifier(ptinOrEfin);
        if (idResult.IsFailure)
            return Result.Failure<PreparerInfo>(idResult.Error);

        var nameResult = ValidateDisplayName(displayName);
        if (nameResult.IsFailure)
            return Result.Failure<PreparerInfo>(nameResult.Error);

        var titleResult = ValidateTitle(titleLabel);
        if (titleResult.IsFailure)
            return Result.Failure<PreparerInfo>(titleResult.Error);

        return Result.Success(new PreparerInfo(idResult.Value, nameResult.Value, titleResult.Value));
    }

    private static Result<string> ValidateIdentifier(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<string>(new Error("Signature.Preparer.IdEmpty", "PTIN or EFIN is required."));

        var normalized = candidate.Trim().ToUpperInvariant();
        if (normalized.Length is < MinIdentifierLength or > MaxIdentifierLength)
            return Result.Failure<string>(
                new Error(
                    "Signature.Preparer.IdLength",
                    $"Identifier must be {MinIdentifierLength}-{MaxIdentifierLength} characters."
                )
            );

        foreach (var c in normalized)
        {
            if (!char.IsLetterOrDigit(c))
                return Result.Failure<string>(
                    new Error("Signature.Preparer.IdFormat", "Identifier must be alphanumeric.")
                );
        }
        return Result.Success(normalized);
    }

    private static Result<string> ValidateDisplayName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<string>(new Error("Signature.Preparer.NameEmpty", "Display name is required."));

        var trimmed = candidate.Trim();
        if (trimmed.Length is < MinDisplayNameLength or > MaxDisplayNameLength)
            return Result.Failure<string>(
                new Error(
                    "Signature.Preparer.NameLength",
                    $"Display name must be between {MinDisplayNameLength} and {MaxDisplayNameLength} characters."
                )
            );
        return Result.Success(trimmed);
    }

    private static Result<string?> ValidateTitle(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Success<string?>(null);

        var trimmed = candidate.Trim();
        if (trimmed.Length > MaxTitleLabelLength)
            return Result.Failure<string?>(
                new Error("Signature.Preparer.TitleLength", $"Title cannot exceed {MaxTitleLabelLength} characters.")
            );
        return Result.Success<string?>(trimmed);
    }
}
