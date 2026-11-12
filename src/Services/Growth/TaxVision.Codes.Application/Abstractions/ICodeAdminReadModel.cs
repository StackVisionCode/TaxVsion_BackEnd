using BuildingBlocks.Common;
using TaxVision.Codes.Application.Definitions.Admin;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;

namespace TaxVision.Codes.Application.Abstractions;

/// <summary>
/// Lecturas de administración de las giftcards (códigos de PLATAFORMA). Solo las consume el PlatformAdmin: las
/// redenciones pertenecen al tenant que canjeó, no a la plataforma, así que el modelo de lectura las cruza de forma
/// explícita y acotada a una definición de plataforma.
/// </summary>
public interface ICodeAdminReadModel
{
    Task<PagedResult<CodeDefinitionDetailsResponse>> ListPlatformDefinitionsAsync(
        int page,
        int size,
        string? status,
        CancellationToken ct = default
    );

    /// <summary>Null si la definición no existe o no es de plataforma.</summary>
    Task<PagedResult<CodeRedemptionRow>?> ListRedemptionsAsync(
        Guid codeDefinitionId,
        int page,
        int size,
        CancellationToken ct = default
    );
}
