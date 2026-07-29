using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Onboarding.ValueObjects;

/// <summary>
/// Hash SHA-256 (hex, 64 chars) del <c>RegistrationToken</c> opaco enviado por correo. El
/// token en claro nunca se persiste ni se publica — solo su hash (PayFlow_Implementation_Plan.md §3.6).
/// </summary>
public sealed record RegistrationTokenHash
{
    private static readonly Regex HexSha256 = new(
        @"^[a-f0-9]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private RegistrationTokenHash(string value) => Value = value;

    public string Value { get; }

    public static Result<RegistrationTokenHash> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!HexSha256.IsMatch(normalized))
        {
            return Result.Failure<RegistrationTokenHash>(
                new Error(
                    "Onboarding.RegistrationTokenHashInvalid",
                    "Registration token hash must be a 64-character lowercase SHA-256 hex digest."
                )
            );
        }

        return Result.Success(new RegistrationTokenHash(normalized));
    }
}
