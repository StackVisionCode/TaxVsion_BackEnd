using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Repositorio de sesiones sin sesiones activas: el gate de sesión única emite normal (no
/// exige takeover). Para los tests que solo materializan una sesión y no ejercen la revocación.</summary>
internal sealed class EmptyUserSessionRepository : ISessionRepository
{
    public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserSession>>([]);

    public Task AddSessionAsync(UserSession session, CancellationToken ct = default) => Task.CompletedTask;

    public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;

    public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult<UserSession?>(null);

    public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult<RefreshToken?>(null);

    public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default
    ) => Task.FromResult(0);

    public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
        Task.FromResult(0);
}

/// <summary>Store de vale de takeover inerte: nunca se ejerce cuando no hay sesiones previas.</summary>
internal sealed class NoopSessionTakeoverTicketStore : ISessionTakeoverTicketStore
{
    public Task<Guid> IssueAsync(SessionTakeoverPayload payload, CancellationToken ct = default) =>
        Task.FromResult(Guid.NewGuid());

    public Task<SessionTakeoverPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default) =>
        Task.FromResult<SessionTakeoverPayload?>(null);
}
