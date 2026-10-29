using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public interface IFormParticipantRepository
{
    Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default);
    Task<FormParticipant?> GetUnlinkedByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);

    // Todos los participantes del email (vinculados o no): la misma persona puede tener varios.
    async Task<IReadOnlyList<Guid>> ListIdsByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        await GetUnlinkedByEmailAsync(tenantId, email, ct) is { } participant ? [participant.Id] : [];
    void Add(FormParticipant participant);

    // Suelta un alta que perdió la carrera contra el índice único, para volver a leer la ganadora.
    void Untrack(FormParticipant participant);
}

public interface IFormIntakeLinkRepository
{
    Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);
    Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default);
    void Add(FormIntakeLink link);
}

public interface IFormAccessGrantRepository
{
    Task<FormAccessGrant?> GetByRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
    Task<FormAccessGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    void Add(FormAccessGrant grant);
}

public interface IFormAccessChallengeRepository
{
    Task<FormAccessChallenge?> GetAsync(Guid tenantId, Guid challengeId, CancellationToken ct = default);
    Task<FormAccessChallenge?> GetLatestAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        CancellationToken ct = default
    );
    Task<int> CountIssuedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime sinceUtc,
        CancellationToken ct = default
    );
    Task<int> CountTenantIssuedAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default);
    Task<bool> HasActiveBlockAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime nowUtc,
        CancellationToken ct = default
    );
    void Add(FormAccessChallenge challenge);
}

public interface IFormAccessSessionRepository
{
    Task<FormAccessSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<FormAccessSession>> ListNotRevokedByRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    );
    void Add(FormAccessSession session);
}
