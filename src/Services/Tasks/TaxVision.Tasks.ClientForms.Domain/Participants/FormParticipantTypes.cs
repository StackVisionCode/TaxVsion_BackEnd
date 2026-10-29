namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public enum FormParticipantStatus
{
    Unverified = 1,
    Verified = 2,
    Linked = 3,
}

public enum FormParticipantSource
{
    DirectedRequest = 1,
    IntakeLink = 2,
}

public enum FormIntakeLinkStatus
{
    Active = 1,
    Paused = 2,
    Revoked = 3,
}
