using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Email normalizado (sin espacios, en minúsculas).</summary>
public sealed record WorkEmail
{
    public const int MaxLength = 320;

    public string Value { get; }

    private WorkEmail(string value) => Value = value;

    public static Result<WorkEmail> Create(string? value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return email.Length is > 0 and <= MaxLength && IsPlausible(email)
            ? Result.Success(new WorkEmail(email))
            : Result.Failure<WorkEmail>(PublicBookingErrors.EmailInvalid);
    }

    private static bool IsPlausible(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || PlainText.HasWhiteSpace(email))
            return false;
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 2;
    }
}
