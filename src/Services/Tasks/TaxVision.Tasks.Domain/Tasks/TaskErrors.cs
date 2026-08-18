using BuildingBlocks.Results;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// Cada código necesita entrada en <c>ErrorHttpMapping</c>; sin ella el error sale como 500.
/// <see cref="BlockedByDependencies"/> mapea a 409, no a 400.
/// </summary>
public static class TaskErrors
{
    public static readonly Error NotFound = new("Task.NotFound", "Task was not found.");

    public static readonly Error OwnerRequired = new(
        "Task.OwnerRequired",
        "Both tenant and creating user are required to create a task."
    );

    /// <summary>La tarea es de otro y quien pide no tiene el override de supervisión.</summary>
    public static readonly Error Forbidden = new(
        "Task.Forbidden",
        "You can only act on your own tasks unless you can manage the whole firm's."
    );

    public static readonly Error TitleEmpty = new("Task.TitleEmpty", "Task title is required.");

    public static readonly Error TitleTooLong = new(
        "Task.TitleTooLong",
        $"Task title cannot exceed {ValueObjects.TaskTitle.MaxLength} characters."
    );

    public static readonly Error DescriptionEmpty = new(
        "Task.DescriptionEmpty",
        "Task description cannot be blank — omit it instead."
    );

    public static readonly Error DescriptionTooLong = new(
        "Task.DescriptionTooLong",
        $"Task description cannot exceed {ValueObjects.TaskDescription.MaxLength} characters."
    );

    public static readonly Error EstimatedHoursNotPositive = new(
        "Task.EstimatedHoursNotPositive",
        "Estimated hours must be greater than zero."
    );

    public static readonly Error EstimatedHoursTooLarge = new(
        "Task.EstimatedHoursTooLarge",
        $"Estimated hours cannot exceed {ValueObjects.EstimatedHours.MaxValue}."
    );

    public static readonly Error AssigneeRequired = new("Task.AssigneeRequired", "An assignee user id is required.");

    public static readonly Error CancellationReasonRequired = new(
        "Task.CancellationReasonRequired",
        "A cancellation reason is required."
    );

    /// <summary>409 y retriable: el contador baja de forma eventual, así que el reintento puede pasar.</summary>
    public static Error BlockedByDependencies(int openBlockerCount) =>
        new("Task.BlockedByDependencies", $"The task is blocked by {openBlockerCount} unfinished dependency(ies).");

    public static Error HasOpenSubtasks(int openSubtaskCount) =>
        new("Task.HasOpenSubtasks", $"The task still has {openSubtaskCount} open subtask(s).");

    public static Error MaxDepthExceeded(int maxDepth) =>
        new("Task.MaxDepthExceeded", $"Subtasks cannot go deeper than {maxDepth + 1} levels.");

    public static Error TooManyChildren(int maxDirectChildren) =>
        new("Task.TooManyChildren", $"A task cannot have more than {maxDirectChildren} direct subtasks.");

    public static readonly Error CannotAddSubtaskToClosedParent = new(
        "Task.CannotAddSubtaskToClosedParent",
        "Cannot add a subtask to a completed or cancelled task."
    );

    public static Error InvalidTransition(TaskItemStatus from, string operation) =>
        new("Task.InvalidTransition", $"Cannot {operation} a task in status {from}.");

    public static class Due
    {
        public static readonly Error NotUtc = new("Task.Due.NotUtc", "The due timestamp must be expressed in UTC.");

        public static readonly Error TimeZoneInvalid = new(
            "Task.Due.TimeZoneInvalid",
            "The time zone must be a valid IANA identifier."
        );

        public static readonly Error StatutoryReasonRequired = new(
            "Task.Due.StatutoryReasonRequired",
            "Postponing or clearing a statutory due date requires an explicit reason."
        );

        public static readonly Error StatutoryReasonTooLong = new(
            "Task.Due.StatutoryReasonTooLong",
            $"The reason cannot exceed {StatutoryChangeReasonMaxLength} characters."
        );
    }

    /// <summary>Tope del texto libre que justifica mover un vencimiento estatutario.</summary>
    public const int StatutoryChangeReasonMaxLength = 500;

    public static class Dependency
    {
        public static readonly Error IdentifiersRequired = new(
            "Task.Dependency.IdentifiersRequired",
            "Tenant and both task identifiers are required."
        );

        public static readonly Error SelfReference = new(
            "Task.Dependency.SelfReference",
            "A task cannot depend on itself."
        );

        public static readonly Error CrossTenant = new(
            "Task.Dependency.CrossTenant",
            "Both tasks must belong to the same tenant."
        );

        public static readonly Error Duplicate = new("Task.Dependency.Duplicate", "That dependency already exists.");

        public static readonly Error Cycle = new("Task.Dependency.Cycle", "That dependency would close a cycle.");

        // No es un ciclo del grafo —son aristas distintas— pero se traba igual: el padre tampoco
        // cierra mientras el hijo siga abierto. La detección de ciclos no lo agarra.
        public static readonly Error AncestorOfSelf = new(
            "Task.Dependency.AncestorOfSelf",
            "A task cannot depend on one of its own descendants."
        );

        public static readonly Error GraphTooLarge = new(
            "Task.Dependency.GraphTooLarge",
            $"The dependency graph exceeds the {Dependencies.TaskDependencyGraph.MaxTraversalNodes}-node traversal limit."
        );

        public static readonly Error NotFound = new("Task.Dependency.NotFound", "Dependency was not found.");
    }

    public static class Timer
    {
        public static readonly Error NotFound = new("Task.Timer.NotFound", "That timer does not belong to the task.");

        public static readonly Error AlreadyRunning = new(
            "Task.Timer.AlreadyRunning",
            "You already have a timer running on this task."
        );

        public static readonly Error NotRunning = new("Task.Timer.NotRunning", "That timer is already stopped.");

        public static readonly Error NotOwner = new(
            "Task.Timer.NotOwner",
            "Only the user who started a timer can stop it."
        );
    }

    public static class Label
    {
        public static readonly Error NotFound = new("Task.Label.NotFound", "Label was not found.");

        public static readonly Error CodeEmpty = new("Task.Label.CodeEmpty", "The label code is required.");

        public static readonly Error CodeTooLong = new(
            "Task.Label.CodeTooLong",
            $"The label code cannot exceed {ValueObjects.TaskLabelCode.MaxLength} characters."
        );

        public static readonly Error CodeInvalid = new(
            "Task.Label.CodeInvalid",
            "The label code accepts lowercase letters, digits and single underscores."
        );

        public static readonly Error CodeTaken = new(
            "Task.Label.CodeTaken",
            "Another label in this tenant already uses that code."
        );

        public static readonly Error DisplayNameEmpty = new(
            "Task.Label.DisplayNameEmpty",
            "The label display name is required."
        );

        public static readonly Error DisplayNameTooLong = new(
            "Task.Label.DisplayNameTooLong",
            $"The label display name cannot exceed {Labels.TaskLabel.DisplayNameMaxLength} characters."
        );

        public static readonly Error ColorInvalid = new(
            "Task.Label.ColorInvalid",
            "The label color must be a hex value such as #2E7D32."
        );
    }

    public static class Reference
    {
        public static readonly Error CustomerInvalid = new(
            "Task.Reference.CustomerInvalid",
            "The customer id cannot be an empty guid — omit it instead."
        );

        public static readonly Error TaxYearOutOfRange = new(
            "Task.Reference.TaxYearOutOfRange",
            $"The tax year must be between {ValueObjects.TaskReference.MinTaxYear} and {ValueObjects.TaskReference.MaxTaxYear}."
        );
    }

    public static class WaitingOnClient
    {
        public static readonly Error ExpectedItemsRequired = new(
            "Task.WaitingOnClient.ExpectedItemsRequired",
            "State what is being requested from the client — the email is useless without it."
        );

        public static readonly Error ExpectedItemsTooLong = new(
            "Task.WaitingOnClient.ExpectedItemsTooLong",
            $"The client request cannot exceed {ValueObjects.ClientRequestNote.MaxLength} characters."
        );

        /// <summary>Código propio: al usuario se le dice que la tarea ya está cerrada.</summary>
        public static readonly Error TaskClosed = new(
            "Task.WaitingOnClient.TaskClosed",
            "A completed or cancelled task cannot be put on hold waiting for the client."
        );

        /// <summary>Sin cliente en la tarea no hay a quién pedirle nada.</summary>
        public static readonly Error CustomerRequired = new(
            "Task.WaitingOnClient.CustomerRequired",
            "The task must reference a customer before it can wait on the client."
        );

        public static readonly Error ClientDueNotUtc = new(
            "Task.WaitingOnClient.ClientDueNotUtc",
            "The client due timestamp must be expressed in UTC."
        );
    }

    public static class Series
    {
        public static readonly Error NotFound = new("Task.Series.NotFound", "Task series was not found.");

        public static readonly Error RuleEmpty = new("Task.Series.RuleEmpty", "The recurrence rule is required.");

        public static readonly Error RuleTooLong = new(
            "Task.Series.RuleTooLong",
            $"The recurrence rule cannot exceed {ValueObjects.RecurrenceRule.MaxLength} characters."
        );

        public static readonly Error RuleInvalid = new(
            "Task.Series.RuleInvalid",
            "The recurrence rule must be a valid RFC 5545 RRULE, such as FREQ=MONTHLY;INTERVAL=3."
        );

        public static readonly Error TimeZoneInvalid = new(
            "Task.Series.TimeZoneInvalid",
            "The recurrence time zone must be a known IANA identifier."
        );

        public static readonly Error AnchorNotUtc = new(
            "Task.Series.AnchorNotUtc",
            "The series anchor must be expressed in UTC."
        );

        public static readonly Error SeedNotUtc = new(
            "Task.Series.SeedNotUtc",
            "The recurrence seed must be expressed in UTC."
        );

        /// <summary>La regla se agotó: tenía UNTIL o COUNT y ya no quedan fechas por delante.</summary>
        public static readonly Error NoFurtherOccurrence = new(
            "Task.Series.NoFurtherOccurrence",
            "The recurrence rule yields no occurrence after that point."
        );

        /// <summary>Materializar con una instancia abierta rompería la invariante de una a la vez.</summary>
        public static readonly Error InstanceStillOpen = new(
            "Task.Series.InstanceStillOpen",
            "The series already has an open instance."
        );

        public static readonly Error NotActive = new(
            "Task.Series.NotActive",
            "Only an active series materializes new occurrences."
        );

        public static readonly Error AlreadyEnded = new("Task.Series.AlreadyEnded", "The series already ended.");

        public static readonly Error MaxOccurrencesInvalid = new(
            "Task.Series.MaxOccurrencesInvalid",
            "The occurrence limit must be greater than zero."
        );

        public static readonly Error EndsBeforeAnchor = new(
            "Task.Series.EndsBeforeAnchor",
            "The series end date cannot precede its anchor."
        );
    }

    public static class Attachment
    {
        public static readonly Error NotFound = new(
            "Task.Attachment.NotFound",
            "The attachment was not found on this task."
        );

        public static readonly Error FileRequired = new("Task.Attachment.FileRequired", "A file id is required.");

        public static readonly Error DisplayNameRequired = new(
            "Task.Attachment.DisplayNameRequired",
            "The attachment display name is required."
        );

        public static readonly Error DisplayNameTooLong = new(
            "Task.Attachment.DisplayNameTooLong",
            "The attachment display name exceeds 260 characters."
        );

        public static readonly Error Duplicate = new(
            "Task.Attachment.Duplicate",
            "That file is already attached to this task."
        );

        public static readonly Error LimitReached = new(
            "Task.Attachment.LimitReached",
            "A task cannot hold more than 20 active attachments."
        );

        public static readonly Error TaskClosed = new(
            "Task.Attachment.TaskClosed",
            "A completed or cancelled task does not take new attachments."
        );
    }

    public static class Template
    {
        public static readonly Error NotFound = new("Task.Template.NotFound", "Task template was not found.");

        public static readonly Error NameRequired = new("Task.Template.NameRequired", "The template name is required.");

        public static readonly Error NameTooLong = new(
            "Task.Template.NameTooLong",
            "The template name cannot exceed 200 characters."
        );

        public static readonly Error StepsRequired = new(
            "Task.Template.StepsRequired",
            "A template needs at least one step."
        );

        public static readonly Error TooManySteps = new(
            "Task.Template.TooManySteps",
            "A template cannot exceed 50 steps."
        );

        public static readonly Error StepOrderInvalid = new(
            "Task.Template.StepOrderInvalid",
            "Step order must be a positive number."
        );

        public static readonly Error DuplicateStepOrder = new(
            "Task.Template.DuplicateStepOrder",
            "Two steps cannot share the same order."
        );

        public static readonly Error StepSelfReference = new(
            "Task.Template.StepSelfReference",
            "A step cannot depend on itself nor be its own parent."
        );

        public static readonly Error StepReferenceMissing = new(
            "Task.Template.StepReferenceMissing",
            "A step references another step that is not part of the template."
        );

        public static readonly Error StepCycle = new("Task.Template.StepCycle", "The step dependencies form a cycle.");

        public static readonly Error ParentCycle = new(
            "Task.Template.ParentCycle",
            "The step hierarchy forms a cycle."
        );

        public static readonly Error Retired = new("Task.Template.Retired", "A retired template cannot be applied.");

        public static readonly Error RecurringNeedsSingleStep = new(
            "Task.Template.RecurringNeedsSingleStep",
            "A recurring template must have exactly one step."
        );

        public static readonly Error TooManyAttachments = new(
            "Task.Template.TooManyAttachments",
            "A template cannot hold more than 20 reference files."
        );

        public static readonly Error DuplicateAttachment = new(
            "Task.Template.DuplicateAttachment",
            "That file is already a reference file of this template."
        );

        public static readonly Error AlreadyApplied = new(
            "Task.Template.AlreadyApplied",
            "This template was already applied to that customer and tax year."
        );
    }
}
