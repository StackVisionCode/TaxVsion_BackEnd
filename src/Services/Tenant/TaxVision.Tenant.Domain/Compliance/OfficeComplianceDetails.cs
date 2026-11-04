using System.Globalization;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>Cumplimiento validado y normalizado (sin el EFIN). Se arma completo antes de tocar el agregado.</summary>
public sealed record OfficeComplianceDetails(
    Guid? SecurityCoordinatorUserId,
    DateOnly? WispAdoptedOn,
    DateOnly? WispLastReviewedOn,
    string? IncidentContactEmail,
    string? IncidentContactPhone,
    int? RetentionYears
)
{
    // La zona más adelantada del planeta es UTC+14: una fecha posterior a "hoy" ahí es futura en todas.
    private static readonly TimeSpan LatestUtcOffset = TimeSpan.FromHours(14);

    public static Result<OfficeComplianceDetails> Create(OfficeComplianceDraft draft, DateTime nowUtc)
    {
        var latestToday = DateOnly.FromDateTime(nowUtc + LatestUtcOffset);

        var adopted = ParsePastDate(draft.WispAdoptedOn, latestToday, OfficeComplianceErrors.WispAdoptedOn);
        if (adopted.IsFailure)
            return Result.Failure<OfficeComplianceDetails>(adopted.Error);

        var reviewed = ParsePastDate(draft.WispLastReviewedOn, latestToday, OfficeComplianceErrors.WispLastReviewedOn);
        if (reviewed.IsFailure)
            return Result.Failure<OfficeComplianceDetails>(reviewed.Error);
        if (adopted.Value is { } adoptedOn && reviewed.Value is { } reviewedOn && reviewedOn < adoptedOn)
            return Result.Failure<OfficeComplianceDetails>(OfficeComplianceErrors.WispLastReviewedOn);

        var email = ContactEmail.CreateOptional(
            draft.IncidentContactEmail,
            OfficeComplianceErrors.IncidentContactEmailCode
        );
        if (email.IsFailure)
            return Result.Failure<OfficeComplianceDetails>(email.Error);

        var phone = ContactPhone.CreateOptional(
            draft.IncidentContactPhone,
            OfficeComplianceErrors.IncidentContactPhoneCode
        );
        if (phone.IsFailure)
            return Result.Failure<OfficeComplianceDetails>(phone.Error);

        var retention = RecordRetentionYears.CreateOptional(draft.RetentionYears);
        if (retention.IsFailure)
            return Result.Failure<OfficeComplianceDetails>(retention.Error);

        return Result.Success(
            new OfficeComplianceDetails(
                draft.SecurityCoordinatorUserId == Guid.Empty ? null : draft.SecurityCoordinatorUserId,
                adopted.Value,
                reviewed.Value,
                email.Value?.Value,
                phone.Value?.Value,
                retention.Value?.Value
            )
        );
    }

    private static Result<DateOnly?> ParsePastDate(string? raw, DateOnly latestToday, Error invalid)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<DateOnly?>(null);

        var parsed = DateOnly.TryParseExact(
            trimmed,
            OfficeComplianceErrors.DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        );
        return parsed && date <= latestToday ? Result.Success<DateOnly?>(date) : Result.Failure<DateOnly?>(invalid);
    }
}
