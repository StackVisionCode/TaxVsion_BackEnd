using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public static class FormRequestErrors
{
    public static readonly Error NotFound = new("FormRequest.NotFound", "The form request was not found.");

    // Reenvío: solo las dirigidas tienen enlace personal, y solo sirve mientras el participante puede editar.
    public static readonly Error NoAccessLink = new(
        "FormRequest.NoAccessLink",
        "This request has no personal access link to resend."
    );
    public static readonly Error ResendNotAllowed = new(
        "FormRequest.ResendNotAllowed",
        "The access link can only be resent while the request is pending or returned."
    );
    public static readonly Error InvalidParticipant = new(
        "FormRequest.InvalidParticipant",
        "The form participant is invalid."
    );
    public static readonly Error ResponsibleUserRequired = new(
        "FormRequest.ResponsibleUserRequired",
        "A responsible user is required for an external participant."
    );
    public static readonly Error TaskRequiresCustomer = new(
        "FormRequest.TaskRequiresCustomer",
        "A task can only be linked to a customer request."
    );
    public static readonly Error IntakeLinkRequired = new(
        "FormRequest.IntakeLinkRequired",
        "An intake link is required for this request origin."
    );
    public static readonly Error InvalidTitle = new("FormRequest.InvalidTitle", "The request title is invalid.");
    public static readonly Error InvalidMessage = new("FormRequest.InvalidMessage", "The client message is invalid.");
    public static readonly Error InvalidTaxYear = new("FormRequest.InvalidTaxYear", "The tax year is invalid.");
    public static readonly Error InvalidDueDate = new(
        "FormRequest.InvalidDueDate",
        "The due date must be in the future."
    );
    public static readonly Error InvalidTransition = new(
        "FormRequest.InvalidTransition",
        "The request cannot make this transition."
    );
    public static readonly Error NotEditable = new("FormRequest.NotEditable", "The request is not editable.");
    public static readonly Error ActorMismatch = new(
        "FormRequest.ActorMismatch",
        "The participant actor does not match this request."
    );
    public static readonly Error AttachmentAlreadyRegistered = new(
        "FormRequest.AttachmentAlreadyRegistered",
        "The file is already registered on this request."
    );
    public static readonly Error TooManyAttachments = new(
        "FormRequest.TooManyAttachments",
        "The attachment limit was reached."
    );
    public static readonly Error AttachmentsNotReady = new(
        "FormRequest.AttachmentsNotReady",
        "All referenced attachments must be available."
    );
    public static readonly Error SensitiveAnswerMissing = new(
        "FormRequest.SensitiveAnswerMissing",
        "A referenced sensitive answer is not available."
    );
    public static readonly Error SensitiveAnswerNotFound = new(
        "FormSensitiveAnswer.NotFound",
        "The sensitive answer was not found."
    );
    public static readonly Error AttachmentNotFound = new(
        "FormRequest.AttachmentNotFound",
        "The attachment was not found."
    );
    public static readonly Error InvalidComponent = new(
        "FormRequest.InvalidComponent",
        "The form component is invalid."
    );
    public static readonly Error InvalidTaxIdentifier = new(
        "FormRequest.InvalidTaxIdentifier",
        "The identifier is not valid."
    );
    public static readonly Error ReminderLimitReached = new(
        "FormRequest.ReminderLimitReached",
        "The reminder limit was reached."
    );
    public static readonly Error AlreadyLinked = new(
        "FormRequest.AlreadyLinked",
        "The request is already linked to another customer."
    );
    public static readonly Error LinkNotBlocked = new(
        "FormRequest.LinkNotBlocked",
        "The customer link is not blocked."
    );
    public static readonly Error VersionMismatch = new(
        "FormRequest.VersionMismatch",
        "The form version does not belong to this tenant."
    );
    public static readonly Error CustomerUnavailable = new(
        "FormRequest.CustomerUnavailable",
        "The customer is not available."
    );
    public static readonly Error TemplateNotPublished = new(
        "FormRequest.TemplateNotPublished",
        "The form template is not published."
    );
    public static readonly Error AlreadyOpenForTemplate = new(
        "FormRequest.AlreadyOpenForTemplate",
        "An open request already exists for this form template."
    );
    public static readonly Error TaskMismatch = new(
        "FormRequest.TaskMismatch",
        "The task does not belong to this customer."
    );
    public static readonly Error PrefillNotAvailable = new(
        "FormRequest.PrefillNotAvailable",
        "Prefilling from another request is not available."
    );
    public static readonly Error CustomerHasOpenRequest = new(
        "FormRequest.CustomerHasOpenRequest",
        "The customer already has an open request for this form template."
    );
    public static readonly Error ResponsibleNotAuthorized = new(
        "FormRequest.ResponsibleNotAuthorized",
        "The responsible user does not have the permissions required by this form's actions."
    );
    public static readonly Error ConcurrencyConflict = new(
        "FormRequest.ConcurrencyConflict",
        "The request was modified by someone else."
    );

    // P-13 — aplicar al perfil.
    public static readonly Error ProfileUpdateNotAllowed = new(
        "FormRequest.ProfileUpdateNotAllowed",
        "The profile can only be updated from a completed request with a known customer."
    );
    public static readonly Error ProfileUpdateInProgress = new(
        "FormRequest.ProfileUpdateInProgress",
        "A profile update is already in progress for this request."
    );
    public static readonly Error ProfileUpdateNoTargets = new(
        "FormRequest.ProfileUpdateNoTargets",
        "No mapped fields were selected to apply to the profile."
    );
    public static readonly Error ProfileUpdateCommandMismatch = new(
        "FormRequest.ProfileUpdateCommandMismatch",
        "The profile update result does not match the pending command."
    );

    // Aplicar destinos fiscales (filing status, tax identifier) exige actor TenantAdmin o PlatformAdmin.
    public static readonly Error ProfileUpdateForbidden = new(
        "FormRequest.ProfileUpdateForbidden",
        "Applying fiscal fields to the profile requires an administrator."
    );
}
