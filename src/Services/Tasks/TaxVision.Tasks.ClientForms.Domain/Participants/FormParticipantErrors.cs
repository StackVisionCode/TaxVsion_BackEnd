using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public static class FormParticipantErrors
{
    public static readonly Error InvalidEmail = new("FormParticipant.InvalidEmail", "The participant email is invalid.");
    public static readonly Error InvalidContact = new("FormParticipant.InvalidContact", "The participant contact is invalid.");
    public static readonly Error InvalidTransition = new("FormParticipant.InvalidTransition", "The participant cannot make this transition.");
    public static readonly Error AlreadyLinked = new("FormParticipant.AlreadyLinked", "The participant is linked to another customer.");
    public static readonly Error IntakeLinkInvalid = new("FormIntakeLink.Invalid", "The intake link is invalid.");
    public static readonly Error IntakeLinkUnavailable = new("FormIntakeLink.Unavailable", "The intake link is not available.");
    public static readonly Error IntakeLimitReached = new("IntakeLink.LimitReached", "The intake link submission limit was reached.");
    public static readonly Error IntakeLinkNotFound = new("FormIntakeLink.NotFound", "The intake link was not found.");
    public static readonly Error IntakeLinkConcurrency = new("FormIntakeLink.ConcurrencyConflict", "The intake link was modified by someone else.");
    public static readonly Error IntakeResponsibleUnavailable = new("FormIntakeLink.ResponsibleUnavailable", "The responsible user is not active.");
    public static readonly Error EntryNoticeRequired = new(
        "intake.entry_notice_required",
        "A published identity verification notice is required."
    );
    public static readonly Error AccessGrantInvalid = new("FormAccessGrant.Invalid", "The access grant is invalid.");
    public static readonly Error AccessGrantUnavailable = new("FormAccessGrant.Unavailable", "The access grant is not available.");
    public static readonly Error AccessGrantIssueLimit = new("FormAccessGrant.IssueLimit", "The access grant issue limit was reached.");
    public static readonly Error ChallengeInvalid = new("FormAccessChallenge.Invalid", "The access challenge is invalid.");
    public static readonly Error ChallengeExpired = new("FormAccessChallenge.Expired", "The access challenge expired.");
    public static readonly Error ChallengeBlocked = new("FormAccessChallenge.Blocked", "The access challenge is temporarily blocked.");
    public static readonly Error ChallengeConsumed = new("FormAccessChallenge.Consumed", "The access challenge was already used.");
    public static readonly Error ChallengeCodeMismatch = new("FormAccessChallenge.CodeMismatch", "The verification code is invalid.");
    public static readonly Error ChallengeCooldown = new("FormAccessChallenge.Cooldown", "Wait before requesting another code.");
    public static readonly Error SessionInvalid = new("FormAccessSession.Invalid", "The form session is invalid.");
    public static readonly Error SessionExpired = new("FormAccessSession.Expired", "The form session expired.");
}
