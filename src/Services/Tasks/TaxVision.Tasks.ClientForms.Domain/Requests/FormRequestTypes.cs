namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public enum FormParticipantKind
{
    Customer = 1,
    External = 2,
}

public enum FormRequestOrigin
{
    Directed = 1,
    IntakeLink = 2,
}

public enum FormRequestStatus
{
    Pending = 1,
    Submitted = 2,
    Returned = 3,
    Completed = 4,
    Cancelled = 5,
}

public enum FormCancelReason
{
    StaffCancelled = 1,
    CustomerArchived = 2,
    Archived = 3,
}

public enum FormProfileUpdateStatus
{
    None = 0,
    Approved = 1,
    Applied = 2,
    Rejected = 3,
}

public enum FormRevisionOutcome
{
    Pending = 0,
    Returned = 1,
    Completed = 2,
}

public enum FormRevisionPdfStatus
{
    None = 0,
    Requested = 1,
    Ready = 2,
    Failed = 3,
}

public enum FormAttachmentStatus
{
    Pending = 1,
    Available = 2,
    Rejected = 3,
    Detached = 4,
}

public enum FormFileVerdictStatus
{
    Available = 1,
    Infected = 2,
    Blocked = 3,
    ScanFailed = 4,
    Deleted = 5,
}

public enum FormTaxIdentifierKind
{
    Ssn = 1,
    Itin = 2,
    Ein = 3,
}

public enum FormActorKind
{
    PortalUser = 1,
    ExternalSession = 2,
}

public enum CustomerLinkStatus
{
    None = 0,
    Linked = 1,
    Blocked = 2,
}

public enum FileVerdictOutcome
{
    Ignored = 0,
    Available = 1,
    Rejected = 2,
    Detached = 3,
}

public enum CustomerLinkResult
{
    Unchanged = 0,
    Linked = 1,
    Blocked = 2,
}
