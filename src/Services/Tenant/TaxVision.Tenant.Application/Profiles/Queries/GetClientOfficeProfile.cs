using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Profiles.Queries;

/// <summary>Tarjeta pública de la oficina para el cliente del portal (tenant del JWT).</summary>
public sealed record GetClientOfficeProfileQuery(Guid TenantId);

public static class GetClientOfficeProfileHandler
{
    public static async Task<Result<ClientOfficeProfileResponse>> Handle(
        GetClientOfficeProfileQuery query,
        ITenantRepository tenants,
        IOfficeProfileRepository profiles,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, ct);
        if (tenant is null)
            return Result.Failure<ClientOfficeProfileResponse>(new Error("Tenant.NotFound", "Tenant does not exist."));

        var profile = await profiles.GetAsync(query.TenantId, ct);
        return Result.Success(From(tenant.Name, tenant.DefaultTimeZoneId, profile));
    }

    private static ClientOfficeProfileResponse From(string officeName, string timeZoneId, OfficeProfile? profile) =>
        profile is null
            ? new ClientOfficeProfileResponse(officeName, null, null, null, null, timeZoneId, [], null, null)
            : new ClientOfficeProfileResponse(
                profile.DisplayName ?? officeName,
                profile.Phone,
                profile.Email,
                profile.Website,
                OfficeProfileResponses.ToResponse(VisitableAddress(profile)),
                timeZoneId,
                profile.BusinessHours.Select(OfficeProfileResponses.ToResponse).ToList(),
                profile.BookingUrl,
                profile.ReviewUrl
            );

    // Postal antes que física; una dirección sin calle o ciudad no sirve para escribir ni llegar.
    private static OfficeAddress? VisitableAddress(OfficeProfile profile) =>
        new[] { profile.MailingAddress, profile.PhysicalAddress }.FirstOrDefault(IsComplete);

    private static bool IsComplete(OfficeAddress? address) =>
        address is not null && !string.IsNullOrWhiteSpace(address.Line1) && !string.IsNullOrWhiteSpace(address.City);
}
