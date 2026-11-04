using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Lecturas del trabajo de un empleado en las solicitudes de firma: lo que escribió sin enviar y donde es
/// el preparador. Lo usan el retiro (offboard), su pre-flight y el cambio de preparador. Siempre por tenant.
/// </summary>
public interface ISignatureRequestStaffWorkRepository
{
    /// <summary>Sin enviar (Draft/Ready/Scheduled) creadas por el usuario, con el grafo completo y seguimiento.</summary>
    Task<IReadOnlyList<SignatureRequest>> ListUnsentAuthoredByAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// No terminales (Draft/Ready/Scheduled/InProgress) cuyo preparador es el usuario y aún no firmó, con el
    /// grafo completo y seguimiento.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListOpenWithPreparerAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>Conteos del pre-flight del retiro.</summary>
    Task<SignatureOffboardingCounts> CountOffboardingImpactAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// La última identidad 8879 que el propio usuario declaró como preparador en este tenant (el PTIN/EFIN
    /// sale siempre del JWT de quien lo declaró, nunca de otro), o null si nunca la declaró.
    /// </summary>
    Task<PreparerIdentitySnapshot?> FindLatestPreparerIdentityAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>Usuarios del tenant que alguna vez declararon su identidad de preparador.</summary>
    Task<IReadOnlyList<Guid>> ListUsersWithPreparerIdentityAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Identidad 8879 declarada (PTIN/EFIN, nombre y título tal como se guardaron).</summary>
public sealed record PreparerIdentitySnapshot(string PtinOrEfin, string DisplayName, string? TitleLabel);

/// <param name="PendingPreparerSignatures">Enviadas o programadas donde es preparador y no firmó.</param>
/// <param name="PreparerOnDrafts">Borradores donde figura como preparador (se le quita).</param>
/// <param name="DraftRequests">Sin enviar escritas por él (pasan al sucesor o se descartan).</param>
public sealed record SignatureOffboardingCounts(int PendingPreparerSignatures, int PreparerOnDrafts, int DraftRequests);
