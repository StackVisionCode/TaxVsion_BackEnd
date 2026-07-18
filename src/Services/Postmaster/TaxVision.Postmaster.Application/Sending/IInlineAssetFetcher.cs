using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Bytes ya descargados de un <see cref="InlineAsset"/>, listos para <c>MimeMessageBuilder</c>.</summary>
public sealed record InlineAssetBytes(string ContentId, byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// Descarga los bytes de un set de <see cref="InlineAsset"/> desde CloudStorage. Valida individual
/// ≤200KB y suma total ≤5MB ANTES de descargar (fail fast) — <see cref="InlineAsset"/> ya garantiza
/// el límite individual en su factory, esta interfaz añade el límite agregado.
/// </summary>
public interface IInlineAssetFetcher
{
    Task<Result<IReadOnlyList<InlineAssetBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<InlineAsset> inlineAssets,
        CancellationToken ct
    );
}
