using Microsoft.Extensions.Options;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.PlatformIssuer;

public sealed class PlatformIssuerProvider(IOptions<PlatformIssuerOptions> options) : IPlatformIssuerProvider
{
    public IssuerSnapshot GetSnapshot()
    {
        var o = options.Value;
        return new IssuerSnapshot(
            o.Name,
            o.TaxId,
            o.AddressLine1,
            o.City,
            o.State,
            o.PostalCode,
            o.Country,
            o.Phone,
            o.Email,
            o.Website,
            o.LogoDataUri
        );
    }
}
