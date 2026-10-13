using BuildingBlocks.Platform;
using Microsoft.Extensions.Options;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.PlatformIssuer;

/// <summary>
/// Emisor de lo que factura la plataforma misma. Nombre legal, correo de facturación y web salen de
/// PlatformSettings (Tenant); dirección, Tax ID y teléfono son datos de la entidad legal y van por config.
/// </summary>
public sealed class PlatformIssuerProvider(IOptions<PlatformIssuerOptions> options, IPlatformSettings platform)
    : IPlatformIssuerProvider
{
    public IssuerSnapshot GetSnapshot()
    {
        var o = options.Value;
        var settings = platform.Current;
        return new IssuerSnapshot(
            settings.LegalName,
            o.TaxId,
            o.AddressLine1,
            o.City,
            o.State,
            o.PostalCode,
            o.Country,
            o.Phone,
            settings.BillingEmail,
            settings.Website,
            o.LogoDataUri
        );
    }
}
