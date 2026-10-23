using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// Una ejecución de una acción configurada. Un reintento es otra fila con Attempt + 1, un CommandId
/// nuevo y la misma clave lógica; la fila anterior queda como historia.
/// </summary>
public sealed class FormActionRun : TenantEntity
{
    public const int ActionKeyMaxLength = 64;
    public const int ActionTypeMaxLength = 32;
    public const int CodeMaxLength = 64;

    private FormActionRun() { }

    public Guid FormRequestId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string ActionKey { get; private set; } = string.Empty;
    public string ActionType { get; private set; } = string.Empty;
    public FormActionRunStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? CommandId { get; private set; }
    public int Attempt { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? ResultRefKind { get; private set; }
    public Guid? ResultRefId { get; private set; }
    public Guid? ExistingCustomerId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorDetail { get; private set; }
    public string? Warnings { get; private set; }
    public DateTime? RetriedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsLatestAttempt => RetriedAtUtc is null;

    // Solo un resultado definitivo que garantiza que no se hizo nada; nunca Requested ni un duplicado.
    public bool IsRetryable =>
        IsLatestAttempt
        && (
            Status == FormActionRunStatus.Failed
            || (
                Status == FormActionRunStatus.Rejected
                && !string.Equals(ErrorCode, FormActionRunErrors.DuplicateFoundCode, StringComparison.Ordinal)
            )
        );

    public static string IdempotencyKeyFor(Guid requestId, int revisionNumber, string actionKey) =>
        $"{requestId:N}:r{revisionNumber}:{actionKey}";

    internal static FormActionRun Create(
        Guid tenantId,
        Guid requestId,
        int revisionNumber,
        string actionKey,
        string actionType,
        string idempotencyKey,
        int attempt,
        Guid? commandId,
        DateTime nowUtc
    )
    {
        var run = new FormActionRun
        {
            FormRequestId = requestId,
            RevisionNumber = revisionNumber,
            ActionKey = actionKey,
            ActionType = actionType,
            Status = FormActionRunStatus.Requested,
            IdempotencyKey = idempotencyKey,
            CommandId = commandId,
            Attempt = attempt,
            RequestedAtUtc = nowUtc,
        };
        run.SetTenant(tenantId);
        return run;
    }

    // Terminal sin comando: la condición no se cumplió, la acción no está disponible o no se pudo armar.
    internal void CloseWithoutCommand(FormActionRunStatus status, string errorCode, DateTime nowUtc)
    {
        Status = status;
        CommandId = null;
        ErrorCode = Truncate(errorCode);
        CompletedAtUtc = nowUtc;
    }

    // El resultado positivo gana sobre un rechazo del mismo comando: el recurso ya existe.
    internal FormActionRunTransition Complete(string resultKind, Guid resultId, string? warnings, DateTime nowUtc)
    {
        if (Status is FormActionRunStatus.Completed or FormActionRunStatus.Skipped)
            return FormActionRunTransition.Ignored;
        Status = FormActionRunStatus.Completed;
        ResultRefKind = resultKind;
        ResultRefId = resultId;
        Warnings = Truncate(warnings);
        ErrorCode = null;
        ErrorDetail = null;
        ExistingCustomerId = null;
        CompletedAtUtc = nowUtc;
        return FormActionRunTransition.Applied;
    }

    internal FormActionRunTransition Reject(string errorCode, Guid? existingCustomerId, string? detail, DateTime nowUtc)
    {
        if (Status != FormActionRunStatus.Requested)
            return FormActionRunTransition.Ignored;
        Status = FormActionRunStatus.Rejected;
        ErrorCode = Truncate(errorCode);
        ErrorDetail = Truncate(detail);
        ExistingCustomerId = existingCustomerId;
        CompletedAtUtc = nowUtc;
        return FormActionRunTransition.Applied;
    }

    internal FormActionRunTransition Fail(string errorCode, DateTime nowUtc)
    {
        if (Status != FormActionRunStatus.Requested)
            return FormActionRunTransition.Ignored;
        Status = FormActionRunStatus.Failed;
        ErrorCode = Truncate(errorCode);
        CompletedAtUtc = nowUtc;
        return FormActionRunTransition.Applied;
    }

    internal void MarkRetried(DateTime nowUtc) => RetriedAtUtc = nowUtc;

    private static string? Truncate(string? value) =>
        value is null || value.Length <= CodeMaxLength ? value : value[..CodeMaxLength];
}
