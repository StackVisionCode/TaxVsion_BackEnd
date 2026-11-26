using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Ubicación aproximada (DB-IP) de una sesión del portal.</summary>
public sealed record PortalSessionLocation(
    string CountryCode,
    string? Region,
    string? City,
    double? Latitude,
    double? Longitude,
    string Source
);

/// <summary>Una sesión de la cuenta del portal del cliente, tal como la devuelve Auth.</summary>
public sealed record PortalSession(
    Guid Id,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    Guid UserId,
    string Channel,
    string Status,
    DateTime? EndedAtUtc,
    string? EndReason,
    bool IsCurrent,
    string? StartIpAddress,
    PortalSessionLocation? Location,
    string? UserDisplayName,
    string? UserActorType,
    bool LocationHidden
);

public sealed record PortalSessionPage(
    IReadOnlyList<PortalSession> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTime FromUtc,
    DateTime ToUtc
);

public sealed record PortalSessionsQuery(DateTime? FromUtc, DateTime? ToUtc, int? Page, int? PageSize);

/// <summary>
/// Sesiones de la cuenta del portal de un cliente en Auth (rutas M2M). Customer comprueba antes el permiso y
/// la visibilidad del cliente.
/// </summary>
public interface ICustomerPortalSessionsClient
{
    Task<Result<PortalSessionPage>> ListSessionsAsync(
        Guid tenantId,
        Guid customerId,
        PortalSessionsQuery query,
        CancellationToken ct = default
    );

    Task<Result> RevokeSessionAsync(
        Guid tenantId,
        Guid customerId,
        Guid sessionId,
        Guid requestedByUserId,
        CancellationToken ct = default
    );

    Task<Result> RevokeAllSessionsAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        bool forgetTrustedDevices,
        CancellationToken ct = default
    );
}
