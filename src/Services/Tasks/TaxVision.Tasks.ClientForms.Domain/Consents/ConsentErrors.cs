using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Consents;

public static class ConsentErrors
{
    public static readonly Error NoticeNotFound = new("ConsentNotice.NotFound", "The consent notice was not found.");
    public static readonly Error NoticeInvalid = new("ConsentNotice.Invalid", "The consent notice is invalid.");
    public static readonly Error NoticeArchived = new("ConsentNotice.Archived", "The consent notice is archived.");
    public static readonly Error NoticeKeyTaken = new(
        "ConsentNotice.KeyTaken",
        "A consent notice with this key already exists."
    );
    public static readonly Error ConcurrencyConflict = new(
        "ConsentNotice.ConcurrencyConflict",
        "The consent notice was modified by someone else."
    );
    public static readonly Error VersionNotFound = new(
        "ConsentNotice.VersionNotFound",
        "The consent notice version was not found."
    );
    public static readonly Error RecordInvalid = new("FormConsentRecord.Invalid", "The consent evidence is invalid.");
    public static readonly Error AlreadyAccepted = new(
        "FormConsentRecord.AlreadyAccepted",
        "The consent was already accepted; withdrawing it is handled by the office."
    );
    public static readonly Error RequiredBeforeCollection = new(
        "answer.consent_required_before_collection",
        "Consent is required before collecting this information."
    );
    public static readonly Error RequiredBeforeSubmit = new(
        "answer.consent_required",
        "Consent is required before submitting this form."
    );
}
