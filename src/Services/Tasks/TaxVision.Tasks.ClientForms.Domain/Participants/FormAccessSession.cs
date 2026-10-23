using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed class FormAccessSession : AggregateRoot
{
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(4);

    private FormAccessSession() { }

    public Guid FormRequestId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public string SessionTokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string ClientIp { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public DateTime? RevokedAtUtc { get; private set; }

    public static Result<FormAccessSession> Create(
        Guid tenantId,
        Guid formRequestId,
        Guid participantId,
        string tokenHash,
        string clientIp,
        string userAgent,
        DateTime nowUtc
    )
    {
        var ip = clientIp?.Trim();
        var agent = userAgent?.Trim();
        if (tenantId == Guid.Empty || formRequestId == Guid.Empty || participantId == Guid.Empty
            || string.IsNullOrWhiteSpace(tokenHash) || string.IsNullOrWhiteSpace(ip) || ip.Length > 64
            || string.IsNullOrWhiteSpace(agent) || agent.Length > 512)
            return Result.Failure<FormAccessSession>(FormParticipantErrors.SessionInvalid);
        var session = new FormAccessSession
        {
            Id = Guid.NewGuid(),
            FormRequestId = formRequestId,
            ParticipantId = participantId,
            SessionTokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(InactivityTimeout),
            ClientIp = ip,
            UserAgent = agent,
        };
        session.SetTenant(tenantId);
        return Result.Success(session);
    }

    public bool IsActiveAt(DateTime nowUtc) =>
        !RevokedAtUtc.HasValue
        && nowUtc < ExpiresAtUtc
        && nowUtc < CreatedAtUtc.Add(AbsoluteTimeout);

    public Result Touch(DateTime nowUtc)
    {
        if (!IsActiveAt(nowUtc))
            return Result.Failure(FormParticipantErrors.SessionExpired);
        LastSeenAtUtc = nowUtc;
        var slidingExpiry = nowUtc.Add(InactivityTimeout);
        var absoluteExpiry = CreatedAtUtc.Add(AbsoluteTimeout);
        ExpiresAtUtc = slidingExpiry < absoluteExpiry ? slidingExpiry : absoluteExpiry;
        return Result.Success();
    }

    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;
}
