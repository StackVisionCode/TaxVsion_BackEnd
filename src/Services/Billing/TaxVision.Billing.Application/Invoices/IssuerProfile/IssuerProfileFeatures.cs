using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.ValueObjects;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Application.Invoices.IssuerProfile;

/// <summary>Datos de la empresa del tenant (emisor de las facturas). Direcciones planas para la UI.</summary>
public sealed record IssuerProfileResponse(
    string Name,
    string? TaxId,
    string? Line1,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    string? Phone,
    string? Email,
    string? Website
);

// ---------------- Get ----------------
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
            return Result.Success(new IssuerProfileResponse("", null, null, null, null, null, "US", null, null, null));

        return Result.Success(
            new IssuerProfileResponse(
                p.Name,
                p.TaxId,
                p.Address?.Line1,
                p.Address?.City,
                p.Address?.State,
                p.Address?.Zip,
                p.Address?.Country,
                p.Phone,
                p.Email,
                p.Website
            )
        );
    }
}

// ---------------- Upsert ----------------
public sealed record UpsertIssuerProfileCommand(
    Guid TenantId,
    string Name,
    string? TaxId,
    string? Line1,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    string? Phone,
    string? Email,
    string? Website
);

public static class UpsertIssuerProfileHandler
{
    public static async Task<Result> Handle(
        UpsertIssuerProfileCommand command,
        IIssuerProfileRepository profiles,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result.Failure(
                new Error("Billing.IssuerProfile.NameRequired", "El nombre de la empresa es requerido.")
            );

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var profile = await profiles.GetByTenantAsync(command.TenantId, ct);
        if (profile is null)
        {
            profile = DomainIssuerProfile.Create(command.TenantId, nowUtc);
            await profiles.AddAsync(profile, ct);
        }

        Address? address =
            string.IsNullOrWhiteSpace(command.Line1) && string.IsNullOrWhiteSpace(command.City)
                ? null
                : new Address(
                    command.Line1 ?? string.Empty,
                    null,
                    command.City ?? string.Empty,
                    command.State ?? string.Empty,
                    command.Zip ?? string.Empty,
                    command.Country ?? "US"
                );

        profile.Update(
            command.Name,
            command.TaxId,
            address,
            command.Phone,
            command.Email,
            command.Website,
            null,
            nowUtc
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
