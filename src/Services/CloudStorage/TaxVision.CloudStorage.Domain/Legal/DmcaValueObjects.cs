using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Legal;

public sealed record ClaimantEmail
{
    private static readonly Regex Format = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private ClaimantEmail(string value) => Value = value;

    public string Value { get; }

    public static Result<ClaimantEmail> Create(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 320 && Format.IsMatch(value)
            ? Result.Success(new ClaimantEmail(value))
            : Result.Failure<ClaimantEmail>(DmcaErrors.InvalidClaimantEmail);
}

public static class DmcaErrors
{
    public static readonly Error NotFound = new("DmcaNotice.NotFound", "The DMCA notice was not found.");
    public static readonly Error InvalidClaimantEmail = new(
        "DmcaNotice.InvalidClaimantEmail",
        "The claimant email address is invalid."
    );
    public static readonly Error SwornStatementRequired = new(
        "DmcaNotice.SwornStatementRequired",
        "The claimant must accept the good-faith/perjury sworn statement to register a takedown."
    );
    public static readonly Error DescriptionRequired = new(
        "DmcaNotice.DescriptionRequired",
        "The copyrighted work and infringing material descriptions are required."
    );
    public static readonly Error InvalidTransition = new(
        "DmcaNotice.InvalidTransition",
        "The requested DMCA notice status transition is invalid."
    );
    public static readonly Error CounterNoticeTextRequired = new(
        "DmcaNotice.CounterNoticeTextRequired",
        "The counter-notice text is required."
    );
    public static readonly Error ActiveNoticeAlreadyExists = new(
        "DmcaNotice.ActiveNoticeAlreadyExists",
        "There is already an open DMCA notice against this file."
    );
}
