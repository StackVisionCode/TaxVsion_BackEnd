using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>
/// Vuelve a anunciar el logo confirmado de cada tenant y superficie (CRM y Portal) para poblar o
/// reparar las proyecciones de Scribe, Documents y Signature. Idempotente: cada evento lleva la fecha
/// de confirmación original y los consumidores hacen upsert por (tenant, superficie), así que correrlo
/// otra vez no cambia nada. Lo dispara el PlatformAdmin cuando los consumidores ya están desplegados.
/// </summary>
public sealed record RepublishTenantLogosCommand;

public sealed record RepublishTenantLogosResult(int Published);

public static class RepublishTenantLogosHandler
{
    private const int PageSize = 200;

    public static async Task<Result<RepublishTenantLogosResult>> Handle(
        RepublishTenantLogosCommand _,
        ITenantBrandRepository repo,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<RepublishTenantLogosCommand> logger,
        CancellationToken ct
    )
    {
        var published = 0;
        var skip = 0;
        while (true)
        {
            var page = await repo.ListConfirmedLogosAsync(skip, PageSize, ct);
            foreach (var logo in page)
            {
                // Superficies que no salen del servicio (Mobile/Email) no se cuentan.
                if (BrandLogoEvents.ContractSurfaceOf(logo.Surface) is null)
                    continue;

                await BrandLogoEvents.PublishUpdatedIfLogoAsync(
                    bus,
                    correlation,
                    logo.TenantId,
                    logo.Surface,
                    BrandAssetKey.Logo,
                    new LogoSnapshot(
                        logo.FileId,
                        logo.ContentType,
                        logo.SizeBytes,
                        logo.Width,
                        logo.Height,
                        logo.ConfirmedAtUtc
                    )
                );
                published++;
            }

            if (page.Count < PageSize)
                break;
            skip += PageSize;
        }

        logger.LogInformation("Tenant logos republished: {Published} logo event(s).", published);
        return Result.Success(new RepublishTenantLogosResult(published));
    }
}
