using TaxVision.Signature.Application.Requests.Queries.Work;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Lectura del trabajo de firmas de unas personas (Work Profile), con el mismo ABAC del listado para quien mira.
/// Solo ids, título, estado y vencimiento: nunca datos de firmantes ni documentos.
/// </summary>
public interface ISignatureWorkReadService
{
    Task<IReadOnlyList<SignatureWorkRow>> ListAsync(SignatureWorkFilter filter, CancellationToken ct = default);

    /// <summary>Títulos de las solicitudes pedidas que quien mira ve en el listado; el resto se omite.</summary>
    Task<IReadOnlyList<SignatureTitleRow>> ListTitlesAsync(SignatureTitleFilter filter, CancellationToken ct = default);
}

/// <param name="Ids">Solicitudes pedidas (como mucho WorkLimits.TitleBatch).</param>
/// <param name="ActorUserId">Quien mira: su asignación de clientes filtra si no ve todo.</param>
public sealed record SignatureTitleFilter(
    Guid TenantId,
    IReadOnlyCollection<Guid> Ids,
    Guid ActorUserId,
    bool CanViewAll
);

/// <param name="UserIds">Personas cuyo trabajo se mide (autor o preparador).</param>
/// <param name="ActorUserId">Quien mira: su asignación de clientes filtra las filas si no ve todo.</param>
/// <param name="ExpiredSinceUtc">Las expiradas cuentan como vencidas solo desde este instante.</param>
public sealed record SignatureWorkFilter(
    Guid TenantId,
    IReadOnlyCollection<Guid> UserIds,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime ExpiredSinceUtc
);
