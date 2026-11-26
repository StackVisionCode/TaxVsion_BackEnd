using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Oficina y si quien consulta ve también las sesiones de los administradores.</summary>
public sealed record SessionHistoryScope(Guid TenantId, bool IncludeAdmins);

/// <summary>Filtros de la lista de sesiones del personal de una oficina (por fecha de inicio).</summary>
public sealed record SessionHistoryFilter(
    SessionHistoryScope Scope,
    DateTime FromUtc,
    DateTime ToUtc,
    SessionActivityWindow Window,
    Guid? UserId = null,
    SessionStatus? Status = null,
    SessionChannel? Channel = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record SessionHistoryRow(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserLastName,
    UserActorType UserActorType,
    string? DeviceName,
    string? UserAgent,
    string? IpAddress,
    string? StartIpAddress,
    SessionChannel Channel,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    DateTime? RevokedAtUtc,
    string? RevokedReason,
    SessionLocation? Location = null
);

public sealed record SessionHistoryPage(IReadOnlyList<SessionHistoryRow> Items, int TotalCount);

/// <summary>Inicios del periodo en un país.</summary>
public sealed record SessionCountryCount(string CountryCode, int Count);

/// <summary>Conteos de la cabecera; <see cref="SignInsUtc"/> son los inicios del periodo, para agruparlos por día.</summary>
public sealed record SessionHistoryCounts(
    int ActiveNow,
    int MembersWithActiveSession,
    int StaffMembers,
    int SignedOutByAdmin,
    IReadOnlyList<DateTime> SignInsUtc,
    IReadOnlyList<SessionCountryCount> ByCountry,
    int WithoutLocation
);

/// <summary>Lectura del historial de sesiones del personal y purga por retención.</summary>
public interface ISessionHistory
{
    Task<SessionHistoryPage> ListAsync(SessionHistoryFilter filter, CancellationToken ct = default);

    /// <summary>Historial de UN usuario de la oficina, sea personal o del portal; por fecha de inicio.</summary>
    Task<SessionHistoryPage> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<SessionHistoryCounts> CountAsync(
        SessionHistoryScope scope,
        DateTime fromUtc,
        DateTime toUtc,
        SessionActivityWindow window,
        CancellationToken ct = default
    );

    /// <summary>Borra sesiones cerradas antes de esa fecha, hasta <paramref name="batchSize"/>. Nunca toca las abiertas.</summary>
    Task<int> PurgeClosedBeforeAsync(DateTime closedBeforeUtc, int batchSize, CancellationToken ct = default);
}
