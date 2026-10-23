namespace TaxVision.Tasks.ClientForms.Domain.Consents;

public enum ConsentNoticeStatus
{
    Active = 1,
    Archived = 2,
}

public enum ConsentNoticePurpose
{
    General = 1,
    IdentityVerification = 2,
}

public enum FormConsentOutcome
{
    Presented = 1,
    Accepted = 2,
    Declined = 3,
}
