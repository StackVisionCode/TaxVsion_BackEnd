using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using DomainAddress = TaxVision.Billing.Domain.ValueObjects.Address;

namespace TaxVision.Billing.Application.Invoices.IssuerProfile;

/// <summary>Datos de la empresa del tenant (emisor de las facturas). Direcciones planas para la UI.</summary>
public sealed record IssuerProfileResponse(
    string Name,
    string? TaxId,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    string? Phone,
    string? Email,
    string? Website,
    string DefaultCurrency
);

// ---------------- Get (solo lectura: el perfil se edita en Tenant) ----------------
public sealed record GetIssuerProfileQuery(Guid TenantId);

public static class GetIssuerProfileHandler
{
    public static async Task<Result<IssuerProfileResponse>> Handle(
        GetIssuerProfileQuery query,
        IIssuerProfileRepository profiles,
        CancellationToken ct
    )
    {
        var p = await profiles.GetByTenantAsync(query.TenantId, ct);
        if (p is null)
            return Result.Success(
                new IssuerProfileResponse("", null, null, null, null, null, null, "US", null, null, null, "USD")
            );

        return Result.Success(
            new IssuerProfileResponse(
                p.Name,
                p.TaxId,
                // Lo guardado se devuelve tal cual, aunque sea de antes de la validación (nunca falla al leer).
                NullIfBlank(p.Address?.Line1),
                p.Address?.Line2,
                NullIfBlank(p.Address?.City),
                NullIfBlank(p.Address?.State),
                NullIfBlank(p.Address?.Zip),
                p.Address?.Country,
                p.Phone,
                p.Email,
                p.Website,
                p.DefaultCurrency
            )
        );
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

// ---------------- Listado interno (M2M) para la importación a Tenant ----------------

/// <summary>Un perfil por tenant, plano. Texto vacío → null; una dirección vacía (o el marcador "—" de
/// antes de F0) sale toda en null.</summary>
public sealed record IssuerProfileExportItem(
    Guid TenantId,
    string? Name,
    string? TaxId,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    string? Phone,
    string? Email,
    string? Website,
    DateTime UpdatedAtUtc
);

public sealed record ListIssuerProfilesQuery(int Page, int Size);

public static class ListIssuerProfilesHandler
{
    public const int MaxSize = 200;

    private const string LegacyAddressPlaceholder = "—";

    public static async Task<PagedResult<IssuerProfileExportItem>> Handle(
        ListIssuerProfilesQuery query,
        IIssuerProfileRepository profiles,
        CancellationToken ct
    )
    {
        var (items, total) = await profiles.ListPageAsync(query.Page, query.Size, ct);
        var rows = items
            .Select(p =>
            {
                var address = Usable(p.Address);
                return new IssuerProfileExportItem(
                    p.TenantId,
                    NullIfBlank(p.Name),
                    NullIfBlank(p.TaxId),
                    NullIfBlank(address?.Line1),
                    NullIfBlank(address?.Line2),
                    NullIfBlank(address?.City),
                    NullIfBlank(address?.State),
                    NullIfBlank(address?.Zip),
                    NullIfBlank(address?.Country),
                    NullIfBlank(p.Phone),
                    NullIfBlank(p.Email),
                    NullIfBlank(p.Website),
                    p.UpdatedAtUtc
                );
            })
            .ToList();

        return new PagedResult<IssuerProfileExportItem>(rows, query.Page, query.Size, total);
    }

    // El marcador viejo cuenta como calle vacía; sin nada útil, no hay dirección.
    private static DomainAddress? Usable(DomainAddress? address)
    {
        if (address is null)
            return null;
        var cleaned = address.Line1 == LegacyAddressPlaceholder ? address with { Line1 = string.Empty } : address;
        return cleaned.IsBlank() ? null : cleaned;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
