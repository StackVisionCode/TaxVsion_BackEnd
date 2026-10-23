using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Domain.Consents;

public sealed class FormConsentRecord : TenantEntity
{
    private FormConsentRecord() { }

    public Guid FormRequestId { get; private set; }
    public int? RevisionNumber { get; private set; }
    public string ComponentId { get; private set; } = string.Empty;
    public FormParticipantKind ParticipantKind { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public FormActor AcceptedBy { get; private set; } = default!;
    public Guid ConsentNoticeId { get; private set; }
    public int NoticeVersionNumber { get; private set; }
    public string NoticeContentHash { get; private set; } = string.Empty;

    // Cuerpo canónico que cubre el hash: la evidencia se verifica sin depender del aviso.
    public string NoticeBodyJson { get; private set; } = string.Empty;
    public string TextSnapshot { get; private set; } = string.Empty;
    public string Language { get; private set; } = string.Empty;
    public string? AcceptanceText { get; private set; }
    public FormConsentOutcome Outcome { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string ClientIp { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;

    public static Result<FormConsentRecord> Create(
        Guid tenantId,
        Guid requestId,
        string componentId,
        FormParticipantKind participantKind,
        Guid? customerId,
        Guid? participantId,
        FormActor actor,
        ConsentNoticeVersion noticeVersion,
        string? acceptanceText,
        FormConsentOutcome outcome,
        string clientIp,
        string userAgent,
        DateTime nowUtc
    )
    {
        var participantValid = participantKind switch
        {
            FormParticipantKind.Customer => customerId is not null && customerId != Guid.Empty && participantId is null,
            FormParticipantKind.External => participantId is not null
                && participantId != Guid.Empty
                && customerId is null,
            _ => false,
        };
        var acceptanceValid =
            outcome == FormConsentOutcome.Accepted
                ? !string.IsNullOrWhiteSpace(acceptanceText) && acceptanceText.Length <= 300
                : acceptanceText is null;
        if (
            tenantId == Guid.Empty
            || requestId == Guid.Empty
            || string.IsNullOrWhiteSpace(componentId)
            || componentId.Length > 64
            || !participantValid
            || !actor.IsValid
            || !acceptanceValid
            || noticeVersion.TenantId != tenantId
            || string.IsNullOrWhiteSpace(clientIp)
            || clientIp.Length > 64
            || string.IsNullOrWhiteSpace(userAgent)
            || userAgent.Length > 512
        )
            return Result.Failure<FormConsentRecord>(ConsentErrors.RecordInvalid);

        var record = new FormConsentRecord
        {
            FormRequestId = requestId,
            ComponentId = componentId,
            ParticipantKind = participantKind,
            CustomerId = customerId,
            ParticipantId = participantId,
            AcceptedBy = actor.Copy(),
            ConsentNoticeId = noticeVersion.ConsentNoticeId,
            NoticeVersionNumber = noticeVersion.VersionNumber,
            NoticeContentHash = noticeVersion.ContentHash,
            NoticeBodyJson = noticeVersion.BodyJson,
            TextSnapshot = noticeVersion.TextSnapshot,
            Language = noticeVersion.Language,
            AcceptanceText = acceptanceText?.Trim(),
            Outcome = outcome,
            RecordedAtUtc = nowUtc,
            ClientIp = clientIp,
            UserAgent = userAgent,
        };
        record.SetTenant(tenantId);
        return Result.Success(record);
    }

    public Result BindToRevision(int revisionNumber)
    {
        if (revisionNumber < 1 || (RevisionNumber.HasValue && RevisionNumber != revisionNumber))
            return Result.Failure(ConsentErrors.RecordInvalid);
        RevisionNumber = revisionNumber;
        return Result.Success();
    }
}
